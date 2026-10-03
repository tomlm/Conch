using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Conch.Services;

namespace Conch.ViewModel
{
    /// <summary>One row of Preferences' Keyboard tab: an action and the keys that run it.</summary>
    public partial class HotkeyBindingViewModel : ObservableObject
    {
        private readonly Action<HotkeyBindingViewModel> _record;
        private readonly Action<HotkeyBindingViewModel> _clear;
        private readonly Action<HotkeyBindingViewModel> _reset;

        public HotkeyBindingViewModel(
            HotkeyAction action,
            Action<HotkeyBindingViewModel> record,
            Action<HotkeyBindingViewModel> clear,
            Action<HotkeyBindingViewModel> reset)
        {
            Action = action;
            _record = record;
            _clear = clear;
            _reset = reset;
        }

        public HotkeyAction Action { get; }

        public string Name => Action.Name;

        /// <summary>The binding as shown: "Alt+F2", "(none)", or a prompt while recording.</summary>
        [ObservableProperty]
        private string _keys = string.Empty;

        [ObservableProperty]
        private bool _isRecording;

        [RelayCommand]
        private void Record() => _record(this);

        [RelayCommand]
        private void Clear() => _clear(this);

        [RelayCommand]
        private void Reset() => _reset(this);
    }
}
