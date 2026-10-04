using Avalonia.Input;
using Conch.Services;
using Conch.Services.Roles;
using Conch.ViewModel;
using Xunit;

namespace Conch.Tests;

/// <summary>Recording, clearing and resetting hotkeys from Preferences' Keyboard tab.</summary>
public class HotkeyPreferencesTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "conch-hotkey-prefs-" + Guid.NewGuid().ToString("n"));

    private readonly AppViewModel _app;
    private readonly SettingsViewModel _preferences;

    public HotkeyPreferencesTests()
    {
        Directory.CreateDirectory(_directory);
        _app = new AppViewModel(new ShellSettings(_directory).Load());
        _preferences = new SettingsViewModel(_app, new RoleRegistry(_ => Array.Empty<IRoleProvider>(), _ => null));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private HotkeyBindingViewModel Row(string id) => _preferences.Hotkeys.Single(r => r.Action.Id == id);

    /// <summary>What the shell's key handler does with a press while Preferences records.</summary>
    private void Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Assert.NotNull(_app.Hotkeys.Capture);
        _app.Hotkeys.Capture!(key, modifiers);
    }

    [Fact]
    public void EveryActionIsListedWithItsDefault()
    {
        Assert.Equal(Hotkeys.Actions.Select(a => a.Default), _preferences.Hotkeys.Select(r => r.Keys));
    }

    [Fact]
    public void RecordingAddsTheNextCombinationAndSavesIt()
    {
        var files = Row(Hotkeys.Files);

        files.RecordCommand.Execute(null);
        Press(Key.F3, KeyModifiers.Alt);

        Assert.Equal("Ctrl+Alt+E, Alt+F3", files.Keys);
        Assert.Equal("Ctrl+Alt+E, Alt+F3", _app.Settings.GetHotkey(Hotkeys.Files));
        Assert.Null(_app.Hotkeys.Capture);
        Assert.Equal(Hotkeys.Files, _app.Hotkeys.ActionFor(Key.F3, KeyModifiers.Alt));
        Assert.Equal(Hotkeys.Files, _app.Hotkeys.ActionFor(Key.E, KeyModifiers.Control | KeyModifiers.Alt));
    }

    [Fact]
    public void ClearingThenRecordingReplaces()
    {
        var files = Row(Hotkeys.Files);

        files.ClearCommand.Execute(null);
        files.RecordCommand.Execute(null);
        Press(Key.F3, KeyModifiers.Alt);

        Assert.Equal("Alt+F3", files.Keys);
    }

    [Fact]
    public void PressingABindingTheActionHasAlreadyChangesNothing()
    {
        var files = Row(Hotkeys.Files);

        files.RecordCommand.Execute(null);
        Press(Key.E, KeyModifiers.Control | KeyModifiers.Alt);

        Assert.Equal("Ctrl+Alt+E", files.Keys);
    }

    [Fact]
    public void SearchHasBothItsDefaults()
    {
        Assert.Equal("Alt+F2, Ctrl+Space", Row(Hotkeys.FocusSearch).Keys);
    }

    [Fact]
    public void AModifierOnItsOwnKeepsRecording()
    {
        Row(Hotkeys.Files).RecordCommand.Execute(null);

        Press(Key.LeftAlt, KeyModifiers.Alt);

        Assert.NotNull(_app.Hotkeys.Capture);
    }

    [Fact]
    public void EscapeCancelsAndKeepsTheOldBinding()
    {
        var files = Row(Hotkeys.Files);

        files.RecordCommand.Execute(null);
        Press(Key.Escape);

        Assert.Equal("Ctrl+Alt+E", files.Keys);
        Assert.Null(_app.Hotkeys.Capture);
        Assert.Null(_app.Settings.GetHotkey(Hotkeys.Files));
    }

    [Fact]
    public void AnotherActionsKeysAreRefusedAndTheClashNamed()
    {
        var files = Row(Hotkeys.Files);

        files.RecordCommand.Execute(null);
        Press(Key.F10, KeyModifiers.Control);

        Assert.Equal("Ctrl+Alt+E", files.Keys);
        Assert.Contains("Maximize", _preferences.KeyboardMessage);
    }

    [Fact]
    public void TheWindowManagersKeysAreRefusedAndWhatTheyDoSaid()
    {
        var files = Row(Hotkeys.Files);

        files.RecordCommand.Execute(null);
        Press(Key.F6, KeyModifiers.Control);

        Assert.Equal("Ctrl+Alt+E", files.Keys);
        Assert.Contains("window manager", _preferences.KeyboardMessage);
    }

    [Fact]
    public void APlainLetterIsRefusedBecauseItWouldStopTyping()
    {
        var files = Row(Hotkeys.Files);

        files.RecordCommand.Execute(null);
        Press(Key.A);

        Assert.Equal("Ctrl+Alt+E", files.Keys);
        Assert.Contains("Ctrl, Alt or Super", _preferences.KeyboardMessage);
    }

    [Fact]
    public void AFunctionKeyOnItsOwnCanBeBound()
    {
        Row(Hotkeys.Files).ClearCommand.Execute(null);
        Row(Hotkeys.Files).RecordCommand.Execute(null);
        Press(Key.F12);

        Assert.Equal("F12", Row(Hotkeys.Files).Keys);
    }

    [Fact]
    public void ClearingLeavesTheActionWithNoKeys()
    {
        Row(Hotkeys.MaximizeWindow).ClearCommand.Execute(null);

        Assert.Equal("(none)", Row(Hotkeys.MaximizeWindow).Keys);
        Assert.Null(_app.Hotkeys.ActionFor(Key.F10, KeyModifiers.Control));
    }

    [Fact]
    public void ResettingBringsTheDefaultBack()
    {
        Row(Hotkeys.MaximizeWindow).ClearCommand.Execute(null);
        Row(Hotkeys.MaximizeWindow).ResetCommand.Execute(null);

        Assert.Equal("Ctrl+F10", Row(Hotkeys.MaximizeWindow).Keys);
        Assert.Null(_app.Settings.GetHotkey(Hotkeys.MaximizeWindow));
    }

    [Fact]
    public void ResettingIsRefusedWhenAnotherActionNowHasTheDefault()
    {
        // Files takes Ctrl+F10 after Maximize gave it up; putting Maximize back on it would
        // leave two actions on one combination.
        Row(Hotkeys.MaximizeWindow).ClearCommand.Execute(null);
        Row(Hotkeys.Files).RecordCommand.Execute(null);
        Press(Key.F10, KeyModifiers.Control);

        Row(Hotkeys.MaximizeWindow).ResetCommand.Execute(null);

        Assert.Equal("(none)", Row(Hotkeys.MaximizeWindow).Keys);
        Assert.Contains("Files", _preferences.KeyboardMessage);
    }

    [Fact]
    public void StoppingWhenTheWindowClosesReleasesTheKeyboard()
    {
        // Left behind, the capture would swallow every key the shell gets.
        Row(Hotkeys.Files).RecordCommand.Execute(null);

        _preferences.StopRecording();

        Assert.Null(_app.Hotkeys.Capture);
        Assert.Equal("Ctrl+Alt+E", Row(Hotkeys.Files).Keys);
    }

    [Fact]
    public void StatusAsTextIsSavedAsItIsChanged()
    {
        _preferences.StatusAsText = true;

        Assert.True(new ShellSettings(_directory).Load().StatusAsText);
    }

    [Fact]
    public void ChangesAreAnnouncedSoTheBarCanCatchUp()
    {
        var raised = 0;
        _app.Settings.Changed += (_, _) => raised++;

        _preferences.StatusAsText = true;

        Assert.Equal(1, raised);
    }
}
