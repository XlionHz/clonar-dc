using System.Windows;
using System.Windows.Controls;
using ClonarDC.Services;

namespace ClonarDC;

public partial class MainWindow
{
    private List<DeviceDto> _devices08 = [];

    private async Task InitializeDevices08Async()
    {
        try
        {
            var claim = await _auth.ClaimCurrentDeviceAsync(_session);
            DeviceLimitText.Text = $"{claim.ActiveDevices} of {claim.DeviceLimit} licensed device slot(s) in use.";
            await RefreshDevices08Async();
        }
        catch (Exception exception)
        {
            DeviceLimitText.Text = "Device registration could not be confirmed: " + exception.Message;
            AddLog("warning", DeviceLimitText.Text);
        }
    }

    private async void RefreshDevices08_Click(object sender, RoutedEventArgs e) =>
        await RefreshDevices08Async();

    private async Task RefreshDevices08Async()
    {
        try
        {
            _devices08 = await _auth.GetDevicesAsync(_session);
            DevicesList.ItemsSource = _devices08.Select(device =>
                $"{(device.Active ? "●" : "○")}  {device.Name}\n" +
                $"First seen {device.FirstSeenAt.LocalDateTime:g}  •  Last seen {device.LastSeenAt.LocalDateTime:g}").ToList();
            var active = _devices08.Count(device => device.Active);
            DeviceLimitText.Text = $"{active} of {_session.License.DeviceLimit} licensed device slot(s) in use.";
            RevokeDeviceButton08.IsEnabled = DevicesList.SelectedIndex >= 0 &&
                                             DevicesList.SelectedIndex < _devices08.Count &&
                                             _devices08[DevicesList.SelectedIndex].Active;
        }
        catch (Exception exception)
        {
            DeviceLimitText.Text = "Could not load licensed devices: " + exception.Message;
            RevokeDeviceButton08.IsEnabled = false;
        }
    }

    private void DevicesList_SelectionChanged08(object sender, SelectionChangedEventArgs e)
    {
        RevokeDeviceButton08.IsEnabled = DevicesList.SelectedIndex >= 0 &&
                                         DevicesList.SelectedIndex < _devices08.Count &&
                                         _devices08[DevicesList.SelectedIndex].Active;
    }

    private async void RevokeDevice08_Click(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedIndex < 0 || DevicesList.SelectedIndex >= _devices08.Count) return;
        var device = _devices08[DevicesList.SelectedIndex];
        if (!device.Active) return;

        if (MessageBox.Show(
                $"Remove '{device.Name}' from this license? Any active session from that device will end immediately.",
                "Remove licensed device",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            await _auth.RevokeDeviceAsync(_session, device.Id);
            AddOperation("Licensed device removed: " + device.Name);
            await RefreshDevices08Async();

            try
            {
                await _auth.GetCurrentSessionAsync(_session);
            }
            catch
            {
                MessageBox.Show(
                    "The current computer was removed, so this session has ended.",
                    "GuildSync",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                LogoutRequested = true;
                Close();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Device management", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ResetSelectedUserDevices08_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.IsAdmin || AdminUsersList.SelectedIndex < 0 || AdminUsersList.SelectedIndex >= _adminUsers.Count)
        {
            MessageBox.Show("Select a user first.", "Administration", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var user = _adminUsers[AdminUsersList.SelectedIndex];
        if (MessageBox.Show(
                $"Remove every licensed device and active session for {user.Email}?",
                "Reset devices",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            await _auth.ResetUserDevicesAsync(_session, user.Id);
            AddOperation("Admin reset devices: " + user.Email);
            await LoadAdminUsersAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Administration", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}