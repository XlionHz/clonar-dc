using System.Net;
using System.Net.Http.Headers;

namespace ClonarDC.Services;

public sealed class GuildSyncEngine : IDisposable
{
    private const string ApiBase = "https://discord.com/api/v10/";
    private readonly DiscordService _captureService;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private string _token = string.Empty;

    public GuildSyncEngine(DiscordService captureService)
    {
        _captureService = captureService;
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            BaseAddress = new Uri(ApiBase),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildSync-Desktop/0.8.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void SetToken(string token)
    {
        token = token.Trim();
        if (token.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase)) token = token[4..].Trim();
        if (token.Length < 20) throw new InvalidOperationException("Token do bot inválido ou incompleto.");
        _token = token;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);
        _captureService.SetToken(token);
    }

    public async Task<(ClonePlan Plan, GuildSnapshot Source)> AnalyzeAsync(
        string sourceGuildId,
        string targetGuildId,
        string mode,
        IProgress<OperationLog>? progress = null,
        CancellationToken ct = default)
    {
        EnsureDifferentGuilds(sourceGuildId, targetGuildId);
        mode = NormalizeMode(mode);
        progress?.Report(Log("info", "Capturando a estrutura de origem para análise…"));
        var source = await _captureService.CaptureAsync(sourceGuildId, progress, ct);
        progress?.Report(Log("info", "Capturando a estrutura atual do destino…"));
        var target = await _captureService.CaptureAsync(targetGuildId, progress, ct);
        NormalizeSnapshot(source);
        NormalizeSnapshot(target);

        var sourceRoles = source.Roles.Where(IsCloneableRole).ToList();
        var targetRoles = target.Roles.Where(IsCloneableRole).ToList();
        var targetRoleGroups = targetRoles.GroupBy(RoleKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var targetChannelGroups = target.Channels.GroupBy(ChannelKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var targetEmojiNames = target.Emojis.Select(EmojiKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var plan = new ClonePlan
        {
            SourceGuildId = source.SourceGuildId,
            SourceGuildName = source.Name,
            TargetGuildId = target.SourceGuildId,
            TargetGuildName = target.Name,
            Mode = mode,
            TargetRolesToDelete = mode == "exact" ? targetRoles.Count : 0,
            TargetChannelsToDelete = mode == "exact" ? target.Channels.Count : 0,
            TargetEmojisToDelete = mode == "exact" ? target.Emojis.Count : 0,
            UnsupportedMemberOverwrites = source.Channels.SelectMany(channel => channel.PermissionOverwrites).Count(overwrite => overwrite.Type == 1)
        };

        foreach (var role in sourceRoles)
        {
            if (!targetRoleGroups.TryGetValue(RoleKey(role), out var matches) || matches.Count == 0)
                plan.RolesToCreate++;
            else if (mode == "merge" && !RoleEquivalent(role, matches[0]))
                plan.RolesToUpdate++;
            else
                plan.RolesToReuse++;
        }

        foreach (var channel in source.Channels)
        {
            if (!targetChannelGroups.TryGetValue(ChannelKey(channel), out var matches) || matches.Count == 0)
                plan.ChannelsToCreate++;
            else if (mode == "merge" && !ChannelEquivalent(channel, matches[0]))
                plan.ChannelsToUpdate++;
            else
                plan.ChannelsToReuse++;
        }

        foreach (var emoji in source.Emojis)
        {
            if (targetEmojiNames.Contains(EmojiKey(emoji))) plan.EmojisToReuse++;
            else plan.EmojisToCreate++;
        }

        if (source.Roles.Any(role => role.Managed))
            plan.Warnings.Add("Cargos gerenciados por bots e integrações serão ignorados.");
        if (plan.UnsupportedMemberOverwrites > 0)
            plan.Warnings.Add($"{plan.UnsupportedMemberOverwrites} sobrescrita(s) específica(s) de membros não podem ser transferidas automaticamente.");
        if (source.Channels.Any(channel => channel.Type is 15 or 16))
            plan.Warnings.Add("Canais de fórum/mídia serão recriados com as propriedades suportadas; tags e definições avançadas ainda não fazem parte do snapshot.");
        if (targetRoleGroups.Any(pair => pair.Value.Count > 1))
            plan.Warnings.Add("O destino contém cargos duplicados por nome; o primeiro cargo compatível será reutilizado.");
        if (targetChannelGroups.Any(pair => pair.Value.Count > 1))
            plan.Warnings.Add("O destino contém canais duplicados na mesma categoria; o primeiro canal compatível será reutilizado.");
        if (plan.IsDestructive)
            plan.Warnings.Add("O modo exact remove a estrutura clonável existente. Um backup preventivo é obrigatório.");

        plan.RiskScore = CalculateRiskScore(plan);
        progress?.Report(Log("success", $"Plano {plan.Id} criado com risco {plan.RiskScore}/100."));
        return (plan, source);
    }

    public async Task<CloneExecutionReport> ExecuteAsync(
        GuildSnapshot source,
        GuildSummary target,
        CloneExecutionOptions options,
        CloneExecutionReport? resumeReport,
        Func<CloneExecutionReport, CancellationToken, Task>? checkpoint,
        IProgress<OperationLog>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        EnsureDifferentGuilds(source.SourceGuildId, target.Id);
        EnsureToken();
        NormalizeSnapshot(source);
        options.Mode = NormalizeMode(options.Mode);

        var report = PrepareReport(source, target, options.Mode, resumeReport);
        await SaveCheckpointAsync(report, checkpoint, ct);

        try
        {
            progress?.Report(Log("info", $"Operação {report.Id} iniciada em modo {options.Mode}."));
            var targetSnapshot = await _captureService.CaptureAsync(target.Id, progress, ct);
            NormalizeSnapshot(targetSnapshot);

            if (options.Mode == "exact")
            {
                await DeleteExistingStructureAsync(targetSnapshot, target.Id, report, options, checkpoint, progress, ct);
                targetSnapshot = await _captureService.CaptureAsync(target.Id, null, ct);
                NormalizeSnapshot(targetSnapshot);
            }

            await RunStepAsync(
                report,
                "guild:settings",
                "guild",
                source.Name,
                source.SourceGuildId,
                async () =>
                {
                    var body = new JsonObject { ["name"] = source.Name };
                    if (!string.IsNullOrWhiteSpace(source.IconData)) body["icon"] = source.IconData;
                    await SendJsonAsync(HttpMethod.Patch, $"guilds/{target.Id}", body, ct);
                    return target.Id;
                },
                options,
                checkpoint,
                progress,
                ct);

            await SynchronizeRolesAsync(source, targetSnapshot, target.Id, report, options, checkpoint, progress, ct);
            await SynchronizeChannelsAsync(source, targetSnapshot, target.Id, report, options, checkpoint, progress, ct);
            await SynchronizeEmojisAsync(source, targetSnapshot, target.Id, report, options, checkpoint, progress, ct);

            if (options.VerifyAfterExecution)
            {
                progress?.Report(Log("info", "Verificando a estrutura final do destino…"));
                var finalSnapshot = await _captureService.CaptureAsync(target.Id, null, ct);
                NormalizeSnapshot(finalSnapshot);
                report.Verification = VerifySnapshot(source, finalSnapshot);
                if (!report.Verification.Passed)
                {
                    report.Warnings.Add("A verificação pós-operação encontrou diferenças. Consulte o relatório detalhado.");
                    progress?.Report(Log("warning", $"Verificação concluída com {report.Verification.Differences.Count} diferença(s)."));
                }
                else
                {
                    progress?.Report(Log("success", "Verificação pós-operação aprovada."));
                }
            }

            report.CompletedAt = DateTimeOffset.UtcNow;
            report.Status = report.Errors.Count > 0
                ? "completed-with-errors"
                : report.Verification is { Passed: false }
                    ? "completed-with-differences"
                    : "completed";
            await SaveCheckpointAsync(report, checkpoint, ct);
            progress?.Report(Log(report.Status == "completed" ? "success" : "warning",
                $"Operação {report.Id} finalizada: {report.CompletedSteps}/{report.Steps.Count} etapas concluídas, {report.FailedSteps} falha(s)."));
            return report;
        }
        catch (OperationCanceledException)
        {
            report.Status = "cancelled";
            report.CompletedAt = DateTimeOffset.UtcNow;
            await SaveCheckpointIgnoringCancellationAsync(report, checkpoint);
            progress?.Report(Log("warning", $"Operação {report.Id} cancelada; o progresso pode ser retomado."));
            throw;
        }
        catch (Exception exception)
        {
            report.Status = "failed";
            report.CompletedAt = DateTimeOffset.UtcNow;
            report.Errors.Add(SafeMessage(exception.Message));
            await SaveCheckpointIgnoringCancellationAsync(report, checkpoint);
            throw new CloneExecutionException("A operação falhou. O progresso foi guardado e pode ser retomado.", report, exception);
        }
    }

    private async Task DeleteExistingStructureAsync(
        GuildSnapshot target,
        string targetGuildId,
        CloneExecutionReport report,
        CloneExecutionOptions options,
        Func<CloneExecutionReport, CancellationToken, Task>? checkpoint,
        IProgress<OperationLog>? progress,
        CancellationToken ct)
    {
        progress?.Report(Log("warning", "Modo exact: removendo canais existentes…"));
        foreach (var channel in target.Channels.OrderByDescending(channel => channel.Type == 4 ? 0 : 1).ThenByDescending(channel => channel.Position))
        {
            await RunStepAsync(
                report,
                $"delete:channel:{channel.Id}",
                "delete-channel",
                channel.Name,
                channel.Id,
                async () =>
                {
                    await DeleteAsync($"channels/{channel.Id}", ct);
                    return channel.Id;
                },
                options,
                checkpoint,
                progress,
                ct);
        }

        progress?.Report(Log("warning", "Modo exact: removendo emojis existentes…"));
        foreach (var emoji in target.Emojis)
        {
            await RunStepAsync(
                report,
                $"delete:emoji:{emoji.Id}",
                "delete-emoji",
                emoji.Name,
                emoji.Id,
                async () =>
                {
                    await DeleteAsync($"guilds/{targetGuildId}/emojis/{emoji.Id}", ct);
                    return emoji.Id;
                },
                options,
                checkpoint,
                progress,
                ct);
        }

        progress?.Report(Log("warning", "Modo exact: removendo cargos clonáveis…"));
        foreach (var role in target.Roles.Where(IsCloneableRole).OrderByDescending(role => role.Position))
        {
            await RunStepAsync(
                report,
                $"delete:role:{role.Id}",
                "delete-role",
                role.Name,
                role.Id,
                async () =>
                {
                    await DeleteAsync($"guilds/{targetGuildId}/roles/{role.Id}", ct);
                    return role.Id;
                },
                options,
                checkpoint,
                progress,
                ct);
        }
    }

    private async Task SynchronizeRolesAsync(
        GuildSnapshot source,
        GuildSnapshot target,
        string targetGuildId,
        CloneExecutionReport report,
        CloneExecutionOptions options,
        Func<CloneExecutionReport, CancellationToken, Task>? checkpoint,
        IProgress<OperationLog>? progress,
        CancellationToken ct)
    {
        report.RoleMap[source.SourceGuildId] = targetGuildId;
        var existing = target.Roles.Where(role => !role.Managed)
            .GroupBy(RoleKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new Queue<RoleSnapshot>(group.OrderByDescending(role => role.Position)), StringComparer.OrdinalIgnoreCase);

        foreach (var role in source.Roles.Where(IsCloneableRole).OrderBy(role => role.Position))
        {
            ct.ThrowIfCancellationRequested();
            var key = RoleKey(role);
            if (options.Mode != "exact" && existing.TryGetValue(key, out var matches) && matches.Count > 0)
            {
                var matched = matches.Peek();
                report.RoleMap[role.Id] = matched.Id;
                if (options.Mode == "safe")
                {
                    MarkReusedStep(report, $"role:{role.Id}", "role", role.Name, role.Id, matched.Id, "Cargo compatível preservado.");
                    await SaveCheckpointAsync(report, checkpoint, ct);
                    progress?.Report(Log("info", $"Cargo preservado: {role.Name}"));
                    continue;
                }

                await RunStepAsync(
                    report,
                    $"role:{role.Id}",
                    "role",
                    role.Name,
                    role.Id,
                    async () =>
                    {
                        await SendJsonAsync(HttpMethod.Patch, $"guilds/{targetGuildId}/roles/{matched.Id}", BuildRoleBody(role), ct);
                        report.RoleMap[role.Id] = matched.Id;
                        return matched.Id;
                    },
                    options,
                    checkpoint,
                    progress,
                    ct);
                continue;
            }

            await RunStepAsync(
                report,
                $"role:{role.Id}",
                "role",
                role.Name,
                role.Id,
                async () =>
                {
                    var created = await SendJsonAsync(HttpMethod.Post, $"guilds/{targetGuildId}/roles", BuildRoleBody(role), ct);
                    var id = RequiredId(created, "cargo");
                    report.RoleMap[role.Id] = id;
                    return id;
                },
                options,
                checkpoint,
                progress,
                ct);
        }

        if (options.ReorderResources)
        {
            await RunStepAsync(
                report,
                "roles:positions",
                "role-order",
                "Ordem dos cargos",
                null,
                async () =>
                {
                    var positions = new JsonArray();
                    foreach (var role in source.Roles.Where(IsCloneableRole).OrderBy(role => role.Position))
                    {
                        if (report.RoleMap.TryGetValue(role.Id, out var mapped))
                            positions.Add(new JsonObject { ["id"] = mapped, ["position"] = Math.Max(1, role.Position) });
                    }
                    if (positions.Count > 0)
                        await SendJsonAsync(HttpMethod.Patch, $"guilds/{targetGuildId}/roles", positions, ct);
                    return targetGuildId;
                },
                options,
                checkpoint,
                progress,
                ct);
        }
    }

    private async Task SynchronizeChannelsAsync(
        GuildSnapshot source,
        GuildSnapshot target,
        string targetGuildId,
        CloneExecutionReport report,
        CloneExecutionOptions options,
        Func<CloneExecutionReport, CancellationToken, Task>? checkpoint,
        IProgress<OperationLog>? progress,
        CancellationToken ct)
    {
        var existing = target.Channels.GroupBy(ChannelKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new Queue<ChannelSnapshot>(group.OrderBy(channel => channel.Position)), StringComparer.OrdinalIgnoreCase);
        var ordered = source.Channels.Where(channel => channel.Type == 4).OrderBy(channel => channel.Position)
            .Concat(source.Channels.Where(channel => channel.Type != 4).OrderBy(channel => channel.Position));

        foreach (var channel in ordered)
        {
            ct.ThrowIfCancellationRequested();
            var logicalKey = ChannelKey(channel);
            if (options.Mode != "exact" && existing.TryGetValue(logicalKey, out var matches) && matches.Count > 0)
            {
                var matched = matches.Peek();
                report.ChannelMap[channel.Id] = matched.Id;
                if (options.Mode == "safe")
                {
                    MarkReusedStep(report, $"channel:{channel.Id}", "channel", ChannelDisplayName(channel), channel.Id, matched.Id, "Canal compatível preservado.");
                    await SaveCheckpointAsync(report, checkpoint, ct);
                    progress?.Report(Log("info", $"Canal preservado: {ChannelDisplayName(channel)}"));
                    continue;
                }

                await RunStepAsync(
                    report,
                    $"channel:{channel.Id}",
                    "channel",
                    ChannelDisplayName(channel),
                    channel.Id,
                    async () =>
                    {
                        var body = BuildChannelBody(channel, report.RoleMap, report.ChannelMap, includeType: false);
                        await SendJsonAsync(HttpMethod.Patch, $"channels/{matched.Id}", body, ct);
                        report.ChannelMap[channel.Id] = matched.Id;
                        return matched.Id;
                    },
                    options,
                    checkpoint,
                    progress,
                    ct);
                continue;
            }

            await RunStepAsync(
                report,
                $"channel:{channel.Id}",
                "channel",
                ChannelDisplayName(channel),
                channel.Id,
                async () =>
                {
                    var body = BuildChannelBody(channel, report.RoleMap, report.ChannelMap, includeType: true);
                    var created = await SendJsonAsync(HttpMethod.Post, $"guilds/{targetGuildId}/channels", body, ct);
                    var id = RequiredId(created, "canal");
                    report.ChannelMap[channel.Id] = id;
                    return id;
                },
                options,
                checkpoint,
                progress,
                ct);
        }

        if (options.ReorderResources)
        {
            await RunStepAsync(
                report,
                "channels:positions",
                "channel-order",
                "Ordem dos canais",
                null,
                async () =>
                {
                    var positions = new JsonArray();
                    foreach (var channel in source.Channels.OrderBy(channel => channel.Position))
                    {
                        if (!report.ChannelMap.TryGetValue(channel.Id, out var mapped)) continue;
                        var item = new JsonObject { ["id"] = mapped, ["position"] = Math.Max(0, channel.Position) };
                        if (channel.ParentId is not null && report.ChannelMap.TryGetValue(channel.ParentId, out var parent))
                            item["parent_id"] = parent;
                        positions.Add(item);
                    }
                    if (positions.Count > 0)
                        await SendJsonAsync(HttpMethod.Patch, $"guilds/{targetGuildId}/channels", positions, ct);
                    return targetGuildId;
                },
                options,
                checkpoint,
                progress,
                ct);
        }
    }

    private async Task SynchronizeEmojisAsync(
        GuildSnapshot source,
        GuildSnapshot target,
        string targetGuildId,
        CloneExecutionReport report,
        CloneExecutionOptions options,
        Func<CloneExecutionReport, CancellationToken, Task>? checkpoint,
        IProgress<OperationLog>? progress,
        CancellationToken ct)
    {
        var existing = target.Emojis.GroupBy(EmojiKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var emoji in source.Emojis)
        {
            ct.ThrowIfCancellationRequested();
            if (options.Mode != "exact" && existing.TryGetValue(EmojiKey(emoji), out var matched))
            {
                MarkReusedStep(report, $"emoji:{emoji.Id}", "emoji", emoji.Name, emoji.Id, matched.Id, "Emoji existente preservado.");
                await SaveCheckpointAsync(report, checkpoint, ct);
                continue;
            }

            if (string.IsNullOrWhiteSpace(emoji.ImageData))
            {
                MarkSkippedStep(report, $"emoji:{emoji.Id}", "emoji", emoji.Name, emoji.Id, "Asset do emoji não está disponível no backup.");
                report.Warnings.Add($"Emoji {emoji.Name} ignorado porque o asset não foi capturado.");
                await SaveCheckpointAsync(report, checkpoint, ct);
                progress?.Report(Log("warning", $"Emoji sem asset ignorado: {emoji.Name}"));
                continue;
            }

            await RunStepAsync(
                report,
                $"emoji:{emoji.Id}",
                "emoji",
                emoji.Name,
                emoji.Id,
                async () =>
                {
                    var created = await SendJsonAsync(
                        HttpMethod.Post,
                        $"guilds/{targetGuildId}/emojis",
                        new JsonObject { ["name"] = emoji.Name, ["image"] = emoji.ImageData },
                        ct);
                    return RequiredId(created, "emoji");
                },
                options,
                checkpoint,
                progress,
                ct);
        }
    }

    private async Task RunStepAsync(
        CloneExecutionReport report,
        string key,
        string kind,
        string name,
        string? sourceId,
        Func<Task<string?>> action,
        CloneExecutionOptions options,
        Func<CloneExecutionReport, CancellationToken, Task>? checkpoint,
        IProgress<OperationLog>? progress,
        CancellationToken ct)
    {
        var existing = report.Steps.FirstOrDefault(step => string.Equals(step.Key, key, StringComparison.Ordinal));
        if (existing is { Status: "completed" or "reused" or "skipped" })
        {
            progress?.Report(Log("info", $"Etapa já concluída no checkpoint: {name}"));
            return;
        }

        var step = existing ?? new CloneOperationStep
        {
            Key = key,
            Kind = kind,
            Name = name,
            SourceId = sourceId
        };
        if (existing is null) report.Steps.Add(step);
        step.Status = "running";
        step.Attempts++;
        step.StartedAt ??= DateTimeOffset.UtcNow;
        step.Message = null;
        await SaveCheckpointAsync(report, checkpoint, ct);

        try
        {
            var targetId = await action();
            step.TargetId = targetId;
            step.Status = "completed";
            step.Message = "Concluído.";
            step.CompletedAt = DateTimeOffset.UtcNow;
            progress?.Report(Log("success", $"{KindLabel(kind)}: {name}"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = SafeMessage(exception.Message);
            step.Status = "failed";
            step.Message = message;
            step.CompletedAt = DateTimeOffset.UtcNow;
            report.Errors.Add($"{kind}:{name}: {message}");
            progress?.Report(Log("error", $"{KindLabel(kind)} {name}: {message}"));
            if (!options.ContinueOnError)
            {
                await SaveCheckpointAsync(report, checkpoint, ct);
                throw;
            }
        }

        await SaveCheckpointAsync(report, checkpoint, ct);
    }

    private static void MarkReusedStep(
        CloneExecutionReport report,
        string key,
        string kind,
        string name,
        string? sourceId,
        string? targetId,
        string message)
    {
        var step = report.Steps.FirstOrDefault(item => item.Key == key) ?? new CloneOperationStep { Key = key, Kind = kind, Name = name, SourceId = sourceId };
        if (!report.Steps.Contains(step)) report.Steps.Add(step);
        step.Status = "reused";
        step.TargetId = targetId;
        step.Message = message;
        step.StartedAt ??= DateTimeOffset.UtcNow;
        step.CompletedAt = DateTimeOffset.UtcNow;
    }

    private static void MarkSkippedStep(CloneExecutionReport report, string key, string kind, string name, string? sourceId, string message)
    {
        var step = report.Steps.FirstOrDefault(item => item.Key == key) ?? new CloneOperationStep { Key = key, Kind = kind, Name = name, SourceId = sourceId };
        if (!report.Steps.Contains(step)) report.Steps.Add(step);
        step.Status = "skipped";
        step.Message = message;
        step.StartedAt ??= DateTimeOffset.UtcNow;
        step.CompletedAt = DateTimeOffset.UtcNow;
    }

    private static CloneExecutionReport PrepareReport(GuildSnapshot source, GuildSummary target, string mode, CloneExecutionReport? resume)
    {
        if (resume is null)
        {
            return new CloneExecutionReport
            {
                SourceGuildId = source.SourceGuildId,
                SourceGuildName = source.Name,
                TargetGuildId = target.Id,
                TargetGuildName = target.Name,
                Mode = mode
            };
        }

        if (!resume.CanResume ||
            !string.Equals(resume.SourceGuildId, source.SourceGuildId, StringComparison.Ordinal) ||
            !string.Equals(resume.TargetGuildId, target.Id, StringComparison.Ordinal) ||
            !string.Equals(resume.Mode, mode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O relatório selecionado não corresponde a esta operação.");

        resume.Status = "running";
        resume.CompletedAt = null;
        return resume;
    }

    private static CloneVerificationReport VerifySnapshot(GuildSnapshot source, GuildSnapshot target)
    {
        var expectedRoles = source.Roles.Where(IsCloneableRole).Select(RoleKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualRoles = target.Roles.Where(IsCloneableRole).Select(RoleKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedChannels = source.Channels.Select(ChannelKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualChannels = target.Channels.Select(ChannelKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedEmojis = source.Emojis.Where(emoji => !string.IsNullOrWhiteSpace(emoji.ImageData)).Select(EmojiKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualEmojis = target.Emojis.Select(EmojiKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingRoles = expectedRoles.Except(actualRoles, StringComparer.OrdinalIgnoreCase).ToList();
        var missingChannels = expectedChannels.Except(actualChannels, StringComparer.OrdinalIgnoreCase).ToList();
        var missingEmojis = expectedEmojis.Except(actualEmojis, StringComparer.OrdinalIgnoreCase).ToList();
        var differences = new List<string>();
        differences.AddRange(missingRoles.Take(30).Select(value => "Cargo ausente: " + value));
        differences.AddRange(missingChannels.Take(30).Select(value => "Canal ausente: " + value));
        differences.AddRange(missingEmojis.Take(30).Select(value => "Emoji ausente: " + value));
        if (missingRoles.Count + missingChannels.Count + missingEmojis.Count > differences.Count)
            differences.Add("Existem diferenças adicionais omitidas deste resumo.");

        return new CloneVerificationReport
        {
            Passed = missingRoles.Count == 0 && missingChannels.Count == 0 && missingEmojis.Count == 0,
            MissingRoles = missingRoles.Count,
            MissingChannels = missingChannels.Count,
            MissingEmojis = missingEmojis.Count,
            Differences = differences
        };
    }

    private static JsonObject BuildRoleBody(RoleSnapshot role) => new()
    {
        ["name"] = role.Name,
        ["permissions"] = role.Permissions,
        ["color"] = role.Color,
        ["hoist"] = role.Hoist,
        ["mentionable"] = role.Mentionable
    };

    private static JsonObject BuildChannelBody(
        ChannelSnapshot channel,
        IReadOnlyDictionary<string, string> roleMap,
        IReadOnlyDictionary<string, string> channelMap,
        bool includeType)
    {
        var body = new JsonObject { ["name"] = channel.Name };
        if (includeType) body["type"] = channel.Type;
        if (channel.ParentId is not null && channelMap.TryGetValue(channel.ParentId, out var parent)) body["parent_id"] = parent;
        if (channel.Type is 0 or 5 or 15 or 16)
        {
            body["topic"] = channel.Topic;
            body["nsfw"] = channel.Nsfw;
            if (channel.RateLimitPerUser is not null) body["rate_limit_per_user"] = channel.RateLimitPerUser;
        }
        if (channel.Type is 2 or 13)
        {
            if (channel.Bitrate is not null) body["bitrate"] = channel.Bitrate;
            if (channel.UserLimit is not null) body["user_limit"] = channel.UserLimit;
        }

        var overwrites = new JsonArray();
        foreach (var overwrite in channel.PermissionOverwrites.Where(overwrite => overwrite.Type == 0))
        {
            if (!roleMap.TryGetValue(overwrite.Id, out var mapped)) continue;
            overwrites.Add(new JsonObject
            {
                ["id"] = mapped,
                ["type"] = 0,
                ["allow"] = overwrite.Allow,
                ["deny"] = overwrite.Deny
            });
        }
        if (overwrites.Count > 0) body["permission_overwrites"] = overwrites;
        return body;
    }

    private async Task<JsonNode> SendJsonAsync(HttpMethod method, string path, JsonNode body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using var response = await SendWithRetryAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(FriendlyDiscordError(response.StatusCode, raw));
        return string.IsNullOrWhiteSpace(raw) ? new JsonObject() : JsonNode.Parse(raw) ?? new JsonObject();
    }

    private async Task DeleteAsync(string path, CancellationToken ct)
    {
        using var response = await SendWithRetryAsync(new HttpRequestMessage(HttpMethod.Delete, path), ct);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return;
        var raw = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(FriendlyDiscordError(response.StatusCode, raw));
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage original, CancellationToken ct)
    {
        EnsureToken();
        await _requestGate.WaitAsync(ct);
        try
        {
            Exception? lastError = null;
            for (var attempt = 1; attempt <= 8; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                using var request = await CloneRequestAsync(original, ct);
                try
                {
                    var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    if ((int)response.StatusCode == 429)
                    {
                        var delay = await ReadRetryDelayAsync(response, attempt, ct);
                        response.Dispose();
                        await Task.Delay(delay, ct);
                        continue;
                    }
                    if ((int)response.StatusCode >= 500 && attempt < 8)
                    {
                        response.Dispose();
                        await Task.Delay(Backoff(attempt), ct);
                        continue;
                    }
                    return response;
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    lastError = exception;
                    if (attempt == 8) break;
                    await Task.Delay(Backoff(attempt), ct);
                }
            }
            throw new InvalidOperationException("A API do Discord permaneceu indisponível após várias tentativas.", lastError);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static async Task<TimeSpan> ReadRetryDelayAsync(HttpResponseMessage response, int attempt, CancellationToken ct)
    {
        var delay = response.Headers.RetryAfter?.Delta;
        if (delay is not null) return ClampDelay(delay.Value);
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            var seconds = JsonNode.Parse(raw)?["retry_after"]?.GetValue<double>() ?? Backoff(attempt).TotalSeconds;
            return ClampDelay(TimeSpan.FromSeconds(seconds));
        }
        catch
        {
            return Backoff(attempt);
        }
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(30_000, 350 * Math.Pow(2, Math.Min(attempt, 6)) + Random.Shared.Next(50, 350)));

    private static TimeSpan ClampDelay(TimeSpan value) =>
        TimeSpan.FromMilliseconds(Math.Clamp(value.TotalMilliseconds, 150, 60_000));

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage source, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (source.Content is not null)
        {
            var bytes = await source.Content.ReadAsByteArrayAsync(ct);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in source.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    private static void NormalizeSnapshot(GuildSnapshot snapshot)
    {
        snapshot.SchemaVersion = Math.Max(snapshot.SchemaVersion, 2);
        var categories = snapshot.Channels.Where(channel => channel.Type == 4)
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Id))
            .ToDictionary(channel => channel.Id, channel => channel.Name, StringComparer.Ordinal);
        foreach (var channel in snapshot.Channels)
        {
            channel.ParentName = channel.ParentId is not null && categories.TryGetValue(channel.ParentId, out var parent)
                ? parent
                : channel.ParentName;
        }
    }

    private static string RoleKey(RoleSnapshot role) => NormalizeName(role.Name);
    private static string EmojiKey(EmojiSnapshot emoji) => NormalizeName(emoji.Name);
    private static string ChannelKey(ChannelSnapshot channel) =>
        $"{channel.Type}:{NormalizeName(channel.ParentName ?? "root")}:{NormalizeName(channel.Name)}";
    private static string ChannelDisplayName(ChannelSnapshot channel) =>
        string.IsNullOrWhiteSpace(channel.ParentName) ? channel.Name : $"{channel.ParentName} / {channel.Name}";
    private static string NormalizeName(string value) => string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    private static bool IsCloneableRole(RoleSnapshot role) => !role.Managed && !string.Equals(role.Name, "@everyone", StringComparison.OrdinalIgnoreCase);

    private static bool RoleEquivalent(RoleSnapshot source, RoleSnapshot target) =>
        source.Permissions == target.Permissions && source.Color == target.Color && source.Hoist == target.Hoist && source.Mentionable == target.Mentionable;

    private static bool ChannelEquivalent(ChannelSnapshot source, ChannelSnapshot target) =>
        source.Type == target.Type &&
        string.Equals(source.Topic, target.Topic, StringComparison.Ordinal) &&
        source.Nsfw == target.Nsfw &&
        source.Bitrate == target.Bitrate &&
        source.UserLimit == target.UserLimit &&
        source.RateLimitPerUser == target.RateLimitPerUser;

    private static int CalculateRiskScore(ClonePlan plan)
    {
        var score = plan.IsDestructive ? 45 : 5;
        score += Math.Min(20, plan.TargetChannelsToDelete / 5);
        score += Math.Min(15, plan.TargetRolesToDelete / 5);
        score += Math.Min(10, plan.UnsupportedMemberOverwrites);
        score += plan.Warnings.Count * 2;
        return Math.Clamp(score, 0, 100);
    }

    private static string RequiredId(JsonNode node, string resource) =>
        node["id"]?.GetValue<string>() ?? throw new InvalidDataException($"O Discord não devolveu o ID do {resource} criado.");

    private static string NormalizeMode(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "safe" => "safe",
        "merge" => "merge",
        "exact" => "exact",
        _ => throw new InvalidOperationException("Modo de clonagem desconhecido.")
    };

    private static void EnsureDifferentGuilds(string sourceGuildId, string targetGuildId)
    {
        EnsureSnowflake(sourceGuildId);
        EnsureSnowflake(targetGuildId);
        if (string.Equals(sourceGuildId, targetGuildId, StringComparison.Ordinal))
            throw new InvalidOperationException("O servidor de origem e o destino não podem ser iguais.");
    }

    private static void EnsureSnowflake(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 16 or > 20 || !value.All(char.IsDigit))
            throw new InvalidOperationException("ID de servidor inválido.");
    }

    private void EnsureToken()
    {
        if (string.IsNullOrWhiteSpace(_token)) throw new InvalidOperationException("Informe o token do bot antes de continuar.");
    }

    private static string FriendlyDiscordError(HttpStatusCode status, string raw)
    {
        string? message = null;
        string? code = null;
        try
        {
            var node = JsonNode.Parse(raw);
            message = node?["message"]?.GetValue<string>();
            code = node?["code"]?.ToString();
        }
        catch { }
        return status switch
        {
            HttpStatusCode.Unauthorized => "Token do bot inválido ou revogado.",
            HttpStatusCode.Forbidden => "O bot não possui permissão ou hierarquia suficiente para esta ação.",
            HttpStatusCode.NotFound => "Recurso não encontrado ou inacessível para o bot.",
            HttpStatusCode.BadRequest => $"O Discord rejeitou os dados enviados{(string.IsNullOrWhiteSpace(message) ? "." : ": " + SafeMessage(message))}",
            _ => $"Discord API HTTP {(int)status}{(string.IsNullOrWhiteSpace(code) ? string.Empty : " / " + code)}: {SafeMessage(message ?? status.ToString())}"
        };
    }

    private static string SafeMessage(string value)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length > 500 ? normalized[..500] : normalized;
    }

    private static string KindLabel(string kind) => kind switch
    {
        "role" => "Cargo processado",
        "channel" => "Canal processado",
        "emoji" => "Emoji processado",
        "delete-role" => "Cargo removido",
        "delete-channel" => "Canal removido",
        "delete-emoji" => "Emoji removido",
        "role-order" => "Ordem de cargos aplicada",
        "channel-order" => "Ordem de canais aplicada",
        _ => "Etapa concluída"
    };

    private static OperationLog Log(string level, string message) => new(DateTimeOffset.Now, level, message);

    private static Task SaveCheckpointAsync(CloneExecutionReport report, Func<CloneExecutionReport, CancellationToken, Task>? checkpoint, CancellationToken ct) =>
        checkpoint is null ? Task.CompletedTask : checkpoint(report, ct);

    private static async Task SaveCheckpointIgnoringCancellationAsync(CloneExecutionReport report, Func<CloneExecutionReport, CancellationToken, Task>? checkpoint)
    {
        if (checkpoint is null) return;
        try { await checkpoint(report, CancellationToken.None); } catch { }
    }

    public void Dispose()
    {
        _http.Dispose();
        _requestGate.Dispose();
    }
}

public sealed class CloneExecutionException : Exception
{
    public CloneExecutionReport Report { get; }

    public CloneExecutionException(string message, CloneExecutionReport report, Exception innerException)
        : base(message, innerException) => Report = report;
}