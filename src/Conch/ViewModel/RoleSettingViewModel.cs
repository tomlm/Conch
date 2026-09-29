using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Conch.Services.Roles;

namespace Conch.ViewModel
{
    /// <summary>
    /// One row of the Default Apps list: a role, and what the user has chosen to serve it.
    /// </summary>
    public partial class RoleSettingViewModel : ObservableObject
    {
        private readonly Action<string, string?> _saveChoice;
        private bool _loading;

        public RoleSettingViewModel(
            string role,
            IEnumerable<IRoleProvider> candidates,
            string? chosenId,
            Action<string, string?> saveChoice)
        {
            Role = role;
            DisplayName = ShellRoles.DisplayName(role);
            _saveChoice = saveChoice;

            Choices = new ObservableCollection<RoleChoiceViewModel>(
                candidates.Select(c => new RoleChoiceViewModel(c)));

            _loading = true;
            Selected = Choices.FirstOrDefault(c => c.Id == chosenId) ?? Choices.FirstOrDefault();
            _loading = false;
        }

        public string Role { get; }

        public string DisplayName { get; }

        public ObservableCollection<RoleChoiceViewModel> Choices { get; }

        /// <summary>True when nothing on this machine can serve the role.</summary>
        public bool IsEmpty => Choices.Count == 0;

        [ObservableProperty]
        private RoleChoiceViewModel? _selected;

        partial void OnSelectedChanged(RoleChoiceViewModel? value)
        {
            // Not while the list is being built: assigning the current value back to settings
            // during construction would write a choice the user never made, turning a default
            // that tracks what is installed into a pin that no longer moves.
            if (_loading)
            {
                return;
            }

            _saveChoice(Role, value?.Id);
        }
    }

    /// <summary>One entry in a role's dropdown.</summary>
    public sealed class RoleChoiceViewModel
    {
        public RoleChoiceViewModel(IRoleProvider provider)
        {
            Id = provider.Id;

            // The suffix says why something is listed but will not run, which is the question
            // a greyed-out entry raises and does not answer.
            var note = provider switch
            {
                { IsBuiltIn: true } => " (built in)",
                { IsAvailable: false } => " (not installed)",
                _ => string.Empty,
            };

            Label = provider.Name + note;
        }

        public string Id { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }
}
