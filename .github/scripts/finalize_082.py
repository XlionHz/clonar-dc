from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import re

root = Path.cwd()


def write(path: str, text: str) -> None:
    target = root / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8", newline="")


def replace_method(path: str, replacement: str) -> None:
    target = root / path
    text = target.read_text(encoding="utf-8")
    pattern = re.compile(r"public void SetToken\(string token\)\s*\{.*?\n\s*\}", re.S)
    updated, count = pattern.subn(replacement, text, count=1)
    if count == 0:
        print(f"SetToken already changed or not found in {path}")
        return
    target.write_text(updated, encoding="utf-8", newline="")


# Generate one physical identity for Windows, shortcuts and installer.
S = 1024
image = Image.new("RGBA", (S, S), (0, 0, 0, 0))
glow = Image.new("RGBA", image.size, (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse((135, 110, 890, 970), fill=(78, 54, 255, 105))
image.alpha_composite(glow.filter(ImageFilter.GaussianBlur(88)))
outer = [(512,54),(873,225),(873,545),(815,703),(715,851),(512,965),(309,851),(209,703),(151,545),(151,225)]
inner = [(512,188),(732,292),(732,532),(690,650),(620,753),(512,819),(404,753),(334,650),(292,532),(292,292)]
outline = [(512,63),(864,231),(864,539),(807,696),(708,842),(512,952),(316,842),(217,696),(160,539),(160,231)]
gradient = Image.new("RGBA", image.size, (0, 0, 0, 0))
pixels = gradient.load()
for y in range(S):
    t = y / (S - 1)
    colour = (int(151 * (1 - t) + 74 * t), int(82 * (1 - t) + 103 * t), 255, 255)
    for x in range(S):
        pixels[x, y] = colour
mask = Image.new("L", image.size, 0)
ImageDraw.Draw(mask).polygon(outer, fill=255)
image.alpha_composite(Image.composite(gradient, Image.new("RGBA", image.size), mask))
draw = ImageDraw.Draw(image)
draw.line(outline + [outline[0]], fill=(222, 207, 255, 255), width=9, joint="curve")
draw.polygon(inner, fill=(8, 12, 32, 255))
draw.line([(512,194),(719,294)], fill=(215,205,255,220), width=6)
draw.line([(305,294),(512,194)], fill=(215,205,255,220), width=6)
draw.arc((329,333,702,706), 36, 326, fill=(142,76,255,255), width=72)
draw.line([(510,486),(700,486),(700,610)], fill=(76,99,255,255), width=74)
draw.polygon([(700,565),(700,641),(642,699),(602,646)], fill=(76,99,255,255))
floor = Image.new("RGBA", image.size, (0, 0, 0, 0))
ImageDraw.Draw(floor).ellipse((270,915,754,942), fill=(99,70,255,130))
image.alpha_composite(floor.filter(ImageFilter.GaussianBlur(20)))
assets = root / "src/ClonarDC.Desktop/Assets"
assets.mkdir(parents=True, exist_ok=True)
for filename in ("GuildSyncLogo.png", "ClonarDCLogo.png"):
    image.save(assets / filename, optimize=True)
for filename in ("GuildSync.ico", "ClonarDC.ico"):
    image.save(assets / filename, sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])

write("src/ClonarDC.Desktop/Services/TokenAuthorization.cs", '''using System.Net.Http.Headers;

namespace ClonarDC.Services;

internal static class TokenAuthorization
{
    public static AuthenticationHeaderValue Create(string? rawValue)
    {
        var transportValue = (rawValue ?? string.Empty).Trim();
        if (AuthenticationHeaderValue.TryParse(transportValue, out var parsed) &&
            !string.IsNullOrWhiteSpace(parsed.Parameter))
            return parsed;

        return new AuthenticationHeaderValue("Bot", transportValue);
    }
}
''')

replace_method("src/ClonarDC.Desktop/Services/DiscordService.cs", '''public void SetToken(string token)
    {
        _token = token ?? string.Empty;
        _http.DefaultRequestHeaders.Authorization = TokenAuthorization.Create(_token);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildSync-Desktop/0.8.2");
    }''')
replace_method("src/ClonarDC.Desktop/Services/GuildSyncEngine.cs", '''public void SetToken(string token)
    {
        _token = token ?? string.Empty;
        _http.DefaultRequestHeaders.Authorization = TokenAuthorization.Create(_token);
        _captureService.SetToken(_token);
    }''')
replace_method("src/ClonarDC.Desktop/Services/DiscordPreflightService.cs", '''public void SetToken(string token)
    {
        _http.DefaultRequestHeaders.Authorization = TokenAuthorization.Create(token);
    }''')

probe = root / "src/ClonarDC.Desktop/Services/DiscordConnectionProbe.cs"
if probe.exists():
    text = probe.read_text(encoding="utf-8")
    text = re.sub(r"if \(token\.Length < 20\)\s*throw new InvalidOperationException\([^;]+;", "", text, flags=re.S)
    text = re.sub(r"if \(me\[\"bot\"\]\?\.GetValue<bool>\(\) != true\)\s*throw new InvalidOperationException\([^;]+;", "", text, flags=re.S)
    text = re.sub(r"public static string NormalizeToken\(string rawToken\)\s*\{.*?\n\s*\}", "public static string NormalizeToken(string rawToken)\n    {\n        return rawToken ?? string.Empty;\n    }", text, count=1, flags=re.S)
    text = text.replace('client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);', 'client.DefaultRequestHeaders.Authorization = TokenAuthorization.Create(token);')
    text = text.replace("Clonar DC", "GuildSync").replace("0.5.3", "0.8.2")
    probe.write_text(text, encoding="utf-8", newline="")

text_extensions = {".cs", ".xaml", ".csproj", ".ps1", ".yml", ".yaml", ".iss", ".md", ".txt", ".json"}
for path in root.rglob("*"):
    if not path.is_file() or path.suffix.lower() not in text_extensions:
        continue
    if ".git" in path.parts or "build-status" in path.parts or path.name == "finalize_082.py":
        continue
    try:
        text = path.read_text(encoding="utf-8")
    except UnicodeDecodeError:
        continue
    updated = text.replace("Clonar DC", "GuildSync").replace("0.8.1", "0.8.2").replace("v0.5.0 alpha", "v0.8.2 alpha")
    if updated != text:
        path.write_text(updated, encoding="utf-8", newline="")

changelog = root / "CHANGELOG.md"
current = changelog.read_text(encoding="utf-8")
if not current.startswith("## 0.8.2"):
    entry = """## 0.8.2 — Login Experience and Input Fixes

- Rebuilt the premium login screen with a real opening animation.
- Unified GuildSync branding across every app window, taskbar and installer asset.
- Renamed the credential field to Token and removed local format/type rejection.
- Made source and destination server IDs directly editable.

"""
    changelog.write_text(entry + current, encoding="utf-8", newline="")

print("GuildSync 0.8.2 finalizer completed.")
