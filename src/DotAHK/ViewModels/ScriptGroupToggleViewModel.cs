using CommunityToolkit.Mvvm.ComponentModel;

namespace DotAHK.ViewModels;

/// <summary>
/// One checkable group row on a script card. It represents a single membership edge
/// in the many-to-many relationship between scripts and profiles, so toggling it only
/// affects that one edge and never touches the script's other memberships.
/// </summary>
public partial class ScriptGroupToggleViewModel : ObservableObject
{
    private readonly Action<string, bool> _onToggled;
    private bool _suppress;

    public ScriptGroupToggleViewModel(string profileName, bool isMember, Action<string, bool> onToggled)
    {
        ProfileName = profileName;
        _onToggled = onToggled;
        _isMember = isMember;
    }

    /// <summary>Name of the group/profile this toggle represents.</summary>
    public string ProfileName { get; }

    /// <summary>Whether the owning script is currently a member of this group.</summary>
    [ObservableProperty]
    private bool _isMember;

    partial void OnIsMemberChanged(bool value)
    {
        if (_suppress)
        {
            return;
        }

        _onToggled(ProfileName, value);
    }

    /// <summary>Updates the checked state without notifying back to the service.</summary>
    public void SetMemberSilently(bool value)
    {
        _suppress = true;
        try
        {
            IsMember = value;
        }
        finally
        {
            _suppress = false;
        }
    }
}
