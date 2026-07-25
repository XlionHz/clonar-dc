using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private CancellationTokenSource? _paymentPollingCts;

    private async void LicensePage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadPaymentPlansAsync();
        await RefreshLicenseFromServerAsync(showErrors: false);
    }

    private async Task LoadPaymentPlansAsync()
    {
        try
        {
            PaymentStatusText.Text = "Loading available plans…";
            var response = await _auth.GetPaymentPlansAsync();
            PaymentPlanBox.ItemsSource = response.Plans;
            PaymentPlanBox.SelectedIndex = response.Plans.Count > 0 ? 0 : -1;
            BuyLicenseButton.IsEnabled = response.CheckoutConfigured && response.WebhookConfigured && response.Plans.Count > 0;
            PaymentEnvironmentText.Text = response.Environment.Equals("test", StringComparison.OrdinalIgnoreCase)
                ? "Test environment — no real charge will be created."
                : "Production environment — this creates a real payment.";

            PaymentStatusText.Text = response.Plans.Count == 0
                ? "No paid plan is currently available."
                : "Choose a plan and continue to Mercado Pago.";
        }
        catch (Exception ex)
        {
            BuyLicenseButton.IsEnabled = false;
            PaymentStatusText.Text = ex.Message;
        }
    }

    private async void BuyLicense_Click(object sender, RoutedEventArgs e)
    {
        if (PaymentPlanBox.SelectedItem is not PaymentPlanDto plan)
        {
            MessageBox.Show("Select a license plan.", "GuildSync", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            BuyLicenseButton.IsEnabled = false;
            PaymentStatusText.Text = "Creating a secure Mercado Pago checkout…";
            var checkout = await _auth.CreateCheckoutAsync(_session, plan.Code);
            Process.Start(new ProcessStartInfo(checkout.CheckoutUrl) { UseShellExecute = true });
            PaymentStatusText.Text = $"Checkout opened. Waiting for payment confirmation for {FormatPrice(plan.Price, plan.Currency)}…";
            StartPaymentPolling(checkout.OrderId);
        }
        catch (Exception ex)
        {
            PaymentStatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Payment could not be started", MessageBoxButton.OK, MessageBoxImage.Error);
            BuyLicenseButton.IsEnabled = true;
        }
    }

    private async void RefreshLicense_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLicenseFromServerAsync(showErrors: true);
        await LoadPaymentPlansAsync();
    }

    private void StartPaymentPolling(string orderId)
    {
        _paymentPollingCts?.Cancel();
        _paymentPollingCts?.Dispose();
        _paymentPollingCts = new CancellationTokenSource();
        _ = PollPaymentAsync(orderId, _paymentPollingCts.Token);
    }

    private async Task PollPaymentAsync(string orderId, CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < 80; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                var order = await _auth.GetPaymentOrderAsync(_session, orderId, cancellationToken);
                PaymentStatusText.Text = $"Payment status: {order.Status}.";

                if (order.Status.Equals("approved", StringComparison.OrdinalIgnoreCase))
                {
                    await RefreshLicenseFromServerAsync(showErrors: false);
                    PaymentStatusText.Text = "Payment approved. Your license is now active.";
                    MessageBox.Show(
                        "Payment approved and the license was activated automatically.",
                        "License activated",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                if (order.Status is "rejected" or "cancelled" or "refunded" or "charged_back" or "amount-mismatch" or "checkout-error")
                {
                    PaymentStatusText.Text = $"Payment was not completed: {order.Status}.";
                    return;
                }
            }

            PaymentStatusText.Text = "Confirmation is taking longer than expected. Use Refresh license after completing the payment.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PaymentStatusText.Text = "Automatic confirmation paused: " + ex.Message;
        }
        finally
        {
            BuyLicenseButton.IsEnabled = PaymentPlanBox.Items.Count > 0;
        }
    }

    private async Task RefreshLicenseFromServerAsync(bool showErrors)
    {
        try
        {
            var current = await _auth.GetCurrentSessionAsync(_session);
            ApplyLicenseToScreen(current.License);
        }
        catch (Exception ex)
        {
            if (showErrors)
                MessageBox.Show(ex.Message, "Could not refresh license", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyLicenseToScreen(LicenseInfo license)
    {
        var expiry = license.ExpiresAt is null ? "No expiration date" : license.ExpiresAt.Value.LocalDateTime.ToString("f");
        LicenseDetails.Text = $"Status: {license.Status}\nExpiration: {expiry}\nDevice limit: {license.DeviceLimit}\n\nLicense validation is performed by the central backend; the client does not decide by itself whether an account is authorized.";
        DashboardLicense.Text = license.Status;
    }

    private static string FormatPrice(decimal price, string currency)
    {
        if (currency.Equals("BRL", StringComparison.OrdinalIgnoreCase))
            return price.ToString("C", CultureInfo.GetCultureInfo("pt-BR"));
        return $"{price:0.00} {currency}";
    }
}