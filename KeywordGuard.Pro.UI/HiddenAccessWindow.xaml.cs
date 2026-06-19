using System.Windows;
using System.Windows.Controls;
using KeywordGuard.Pro.UI.ViewModels;

namespace KeywordGuard.Pro.UI;

public partial class HiddenAccessWindow : Window
{
    private readonly MainViewModel _viewModel;
    private string? _selectedOriginalValue;

    public HiddenAccessWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        ConfigureAuthState();
    }

    private void ConfigureAuthState()
    {
        bool hasPin = _viewModel.HasConfiguredPin();

        PinInput.Password = "";
        NewPinInput.Password = "";

        AuthPanel.Visibility = Visibility.Visible;
        ContentPanel.Visibility = Visibility.Collapsed;

        EnterPinPanel.Visibility = hasPin ? Visibility.Visible : Visibility.Collapsed;
        PinSetupPanel.Visibility = hasPin ? Visibility.Collapsed : Visibility.Visible;
        ForgotPinButton.Visibility = hasPin && !_viewModel.IsTimerRunning ? Visibility.Visible : Visibility.Collapsed;

        AuthInfoText.Text = hasPin
            ? "Bitte PIN eingeben, um die gespeicherten Blockierungen und den aktuellen Status zu sehen."
            : "Beim ersten Zugriff bitte einen eigenen 4-stelligen PIN erstellen.";
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.VerifyPin(PinInput.Password))
        {
            MessageBox.Show("PIN ist ungültig.", "Fehler");
            return;
        }

        ShowContent();
    }

    private void SavePinButton_Click(object sender, RoutedEventArgs e)
    {
        string pin = NewPinInput.Password;

        bool hasPin = _viewModel.HasConfiguredPin();
        bool success = hasPin ? _viewModel.ResetPin(pin) : _viewModel.CreatePin(pin);
        if (!success)
        {
            string msg = hasPin
                ? "PIN konnte nicht geändert werden. Nur bei deaktiviertem Timer und gültigem 4-stelligem PIN."
                : "PIN konnte nicht gespeichert werden. Bitte genau 4 Ziffern eingeben.";
            MessageBox.Show(msg, "Fehler");
            return;
        }

        MessageBox.Show(hasPin ? "PIN wurde aktualisiert." : "PIN wurde gespeichert.", "Erfolg");
        ShowContent();
    }

    private void ForgotPinButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsTimerRunning)
        {
            MessageBox.Show("Solange der Timer aktiv ist, darf der PIN nicht geändert werden.", "Hinweis");
            return;
        }

        PinSetupPanel.Visibility = Visibility.Visible;
        PinSetupLabel.Text = "Neuen PIN festlegen (4-stellig):";
        NewPinInput.Focus();
    }

    private void ShowContent()
    {
        AuthPanel.Visibility = Visibility.Collapsed;
        ContentPanel.Visibility = Visibility.Visible;
        RefreshContent();
    }

    private void RefreshContent()
    {
        TimerStateText.Text = _viewModel.IsTimerRunning ? "aktiv" : "nicht aktiv";
        BlockingStateText.Text = _viewModel.StatusText;

        bool canEdit = !_viewModel.IsTimerRunning;
        EditLockText.Text = canEdit
            ? "Timer ist aus: Blockierungen können bearbeitet oder gelöscht werden."
            : "Timer ist aktiv: Blockierungen sind sichtbar, aber Änderungen und Löschung sind gesperrt.";

        EditValueTextBox.IsEnabled = canEdit;
        EditAggressiveCheckBox.IsEnabled = canEdit;

        StoredItemsListBox.ItemsSource = _viewModel.GetStoredKeywords();
        ClearEditor();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.TryAddStoredKeyword(EditValueTextBox.Text, EditAggressiveCheckBox.IsChecked == true, out string message))
        {
            RefreshContent();
        }
        else
        {
            MessageBox.Show(message, "Hinweis");
        }
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedOriginalValue))
        {
            MessageBox.Show("Bitte zuerst einen Eintrag auswählen.", "Hinweis");
            return;
        }

        if (_viewModel.TryUpdateStoredKeyword(_selectedOriginalValue, EditValueTextBox.Text, EditAggressiveCheckBox.IsChecked == true, out string message))
        {
            RefreshContent();
        }
        else
        {
            MessageBox.Show(message, "Hinweis");
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (StoredItemsListBox.SelectedItem is not KeywordDisplayItem selected)
        {
            MessageBox.Show("Bitte einen Eintrag auswählen.", "Hinweis");
            return;
        }

        if (_viewModel.TryDeleteStoredKeyword(selected.Value, out string message))
        {
            RefreshContent();
        }
        else
        {
            MessageBox.Show(message, "Hinweis");
        }
    }

    private void StoredItemsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StoredItemsListBox.SelectedItem is not KeywordDisplayItem selected) return;
        _selectedOriginalValue = selected.Value;
        EditValueTextBox.Text = selected.Value;
        EditAggressiveCheckBox.IsChecked = selected.IsAggressive;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContentPanel.Visibility == Visibility.Visible)
            RefreshContent();
        else
            ConfigureAuthState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ClearEditor()
    {
        _selectedOriginalValue = null;
        EditValueTextBox.Text = "";
        EditAggressiveCheckBox.IsChecked = false;
        StoredItemsListBox.SelectedItem = null;
    }
}
