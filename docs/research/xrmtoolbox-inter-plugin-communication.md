# XrmToolBox inter-plugin communication: can one plugin open another?

Research note for a possible future feature: BuMatrixSecurityRoleAssigner handing off
context (a selected team/user/BU) to "User Team Role Inspector with Matrix BU"
(source: sibling repo `../User Team Role Inspector with Matrix BU`), or vice versa.

## Direct answer

**Yes — there is a first-class, host-mediated API for this.** XrmToolBox ships a
message-bus mechanism, `IMessageBusHost`, in `XrmToolBox.Extensibility.Interfaces`. A
plugin control implements this interface to both *send* a message that asks the host to
open (or bring to front) another tool by name, optionally carrying a data payload, and to
*receive* such a message if another tool targets it. The host's `MainForm` acts as the
message broker: it resolves the target plugin by name, opens/activates it, and calls its
`OnIncomingMessage`.

This is not a niche or undocumented trick — it is a stable, deliberately public
extensibility point that real, shipped tools use for exactly this purpose (see FetchXML
Builder below).

## The API surface

### `IMessageBusHost`

Namespace: `XrmToolBox.Extensibility.Interfaces`.
Source: `XrmToolBox.Extensibility/Interfaces/IMessageBusHost.cs` in
[MscrmTools/XrmToolBox](https://github.com/MscrmTools/XrmToolBox) (fetched via raw GitHub
content, master branch).

```csharp
public interface IMessageBusHost
{
    event EventHandler<MessageBusEventArgs> OnOutgoingMessage;
    void OnIncomingMessage(MessageBusEventArgs message);
}
```

- `OnOutgoingMessage` — event a plugin control raises to tell the host "dispatch this
  message." The host (`MainForm`) subscribes to this event on every loaded plugin control
  and does the actual routing/opening.
- `OnIncomingMessage(MessageBusEventArgs message)` — method the host calls on the *target*
  plugin's control once it has been located/opened, delivering the payload.

### `MessageBusEventArgs`

Source: `XrmToolBox.Extensibility/Args/MessageBusEventArgs.cs` in the same repo (fetched
via raw GitHub content, master branch).

```csharp
public class MessageBusEventArgs : EventArgs
{
    public MessageBusEventArgs(string targetPlugin, bool newInstance = false);

    public string TargetPlugin { get; }      // name of the plugin to start/activate
    public bool NewInstance { get; set; }    // force a new instance even if one is open
    public string SourcePlugin { get; set; } // resolved by the broker if not set
    public dynamic TargetArgument { get; set; } // arbitrary payload
}
```

`TargetPlugin` is matched against the target plugin's MEF `ExportMetadata("Name", ...)` —
i.e. the same string shown as the tool's display name in the XrmToolBox tool list/tile.
`TargetArgument` is `dynamic`, so any serializable-enough .NET object can be passed
in-process (this all happens inside the same host AppDomain/process — no serialization
across process boundaries is needed).

### How it's used in practice — FetchXML Builder

FetchXML Builder (a very widely used XrmToolBox tool) documents and implements exactly
this pattern for other tools to open it with a pre-filled FetchXML query, and to get the
edited query back:

Source: [Integrate with FetchXML Builder – JonasR.app](https://jonasr.app/fxb/integrate/).

Sending (opening FXB with data):
```csharp
OnOutgoingMessage(this, new MessageBusEventArgs("FetchXML Builder")
{
    TargetArgument = textBox1.Text // the FetchXML to load
});
```

Receiving (FXB's response coming back to the caller):
```csharp
public void OnIncomingMessage(MessageBusEventArgs message)
{
    if (message.SourcePlugin == "FetchXML Builder" &&
        message.TargetArgument is string fetchxml &&
        !string.IsNullOrWhiteSpace(fetchxml))
    {
        textBox1.Text = fetchxml;
    }
}
```

This confirms: (a) targeting by plugin display-name string works and is the documented
contract, (b) the payload can flow both ways (send data in, get a result back), (c) this
is a real, production pattern other tool authors rely on — not a workaround.

### Reference implementation in the XrmToolBox repo itself

`Plugins/MsCrmTools.SampleTool/SampleTool.cs` (the official sample tool shipped in the
XrmToolBox repo) implements `IMessageBusHost` alongside its other capability interfaces
(`IGitHubPlugin`, `IHelpPlugin`, `IStatusBarMessenger`, etc.):

```csharp
public partial class SampleTool : PluginControlBase, IGitHubPlugin, ICodePlexPlugin,
    IPayPalPlugin, IHelpPlugin, IStatusBarMessenger, IShortcutReceiver, IAboutPlugin,
    IDuplicatableTool, ISettingsPlugin, IPrivatePlugin, IMessageBusHost
{
    ...
    #region IMessageBusHost

    public event EventHandler<MessageBusEventArgs> OnOutgoingMessage;

    public void OnIncomingMessage(MessageBusEventArgs message)
    {
        MessageBox.Show($"I received the following information:\n\n{message.TargetArgument}",
            "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SendMessage(string message)
    {
        OnOutgoingMessage?.Invoke(this, new MessageBusEventArgs("targetPlugin", false));
    }

    #endregion IMessageBusHost
}
```
Source: `Plugins/MsCrmTools.SampleTool/SampleTool.cs`,
[MscrmTools/XrmToolBox](https://github.com/MscrmTools/XrmToolBox) (raw GitHub content,
master branch).

## Limitations / things to keep in mind

- **Opt-in on both ends.** The mechanism only works if *both* the sending plugin and the
  receiving plugin implement `IMessageBusHost`. There's no way to open an arbitrary tool
  that doesn't implement it and have it "just accept" a payload — the host can still
  activate/switch to it (plugins are listed and selectable in the host UI regardless), but
  it has no way to *deliver* your data if the target doesn't implement
  `OnIncomingMessage`.
- **Name-based targeting, not type-based.** `TargetPlugin`/`SourcePlugin` matching is
  string-based against the MEF `Name` export metadata, not a compile-time reference — the
  two plugin projects never need to reference each other's assemblies, but a rename of the
  target tool's `ExportMetadata("Name", ...)` silently breaks the handoff (no compiler
  error, just a message that never finds a home).
- **Same-process, dynamic payload.** `TargetArgument` is `dynamic` and passed in-process
  (no cross-process serialization contract is enforced). If both tools are written by the
  same author (as here), that's fine — pass a POCO or primitive and have both sides agree
  on its shape informally. If you want a stable contract across independently-versioned
  tools, you'd still want to agree on a plain type (e.g. a string-encoded key, or a simple
  DTO with only primitive members) since there's no shared interop assembly unless you
  build one.
- **Requires the target to be installed.** The host can only open/activate a plugin that's
  actually installed in the user's XrmToolBox (via the Tool Library or manual copy). If
  "User Team Role Inspector with Matrix BU" isn't installed, `TargetPlugin` resolution
  presumably fails silently or is a no-op (not confirmed from source excerpts fetched;
  worth a quick live test before relying on it) — the CLAUDE.md-documented plugin
  isolation model (MEF discovery, host supplies all XTB/SDK assemblies, no shared state
  between plugin DLLs) means there's no compile-time guarantee the target exists.
- **No API found to open a tool with *zero* payload participation from the target** beyond
  what `MainForm` already offers via the normal tool-selection UI (i.e., you can't "just
  switch tabs" to another tool without going through the message-bus contract — or without
  the user manually clicking it in the tool list). No `MainForm.OpenTool(string
  pluginName)`-style public method was found documented or referenced anywhere in search
  results; the message bus is the one confirmed public surface for this.

## Recommendation for this repo

Given both plugins here are authored by the same person and already share the same
Dataverse schema conventions (per `CLAUDE.md`: `team`, `systemuser`, `role`, `businessunitid`,
etc.), the message-bus route is a clean, low-risk fit:

1. Add `IMessageBusHost` to `BuMatrixSecurityRoleAssignerControl` (and, symmetrically, to
   `UserTeamRoleInspectorControl` in the other repo) — both already derive from
   `PluginControlBase`, so this is purely an additional interface + two members.
2. Define a small, shared-by-convention payload shape (no shared assembly needed — just
   document the field names in both `CLAUDE.md`/`CONTEXT.md` files), e.g.:
   ```csharp
   // Sent as MessageBusEventArgs.TargetArgument
   public class RoleAssignerHandoff
   {
       public bool IsUser;        // true = user, false = team
       public Guid TargetId;      // team/user id
       public string TargetName;  // for display while the target tool re-resolves TargetId
       public Guid? BusinessUnitId; // optional BU context
   }
   ```
3. In `BuMatrixSecurityRoleAssignerControl`, add a context-menu/button action on the
   selected team/user row, e.g. "Inspect roles..." that raises:
   ```csharp
   public event EventHandler<MessageBusEventArgs> OnOutgoingMessage;

   private void OpenInInspector(IAssignmentTarget target)
   {
       OnOutgoingMessage?.Invoke(this, new MessageBusEventArgs("User/Team Role Inspector")
       {
           TargetArgument = new RoleAssignerHandoff
           {
               IsUser = target is UserItem,
               TargetId = target.Id,
               TargetName = target.Name
           }
       });
   }
   ```
   Note: `"User/Team Role Inspector"` must exactly match the target's
   `ExportMetadata("Name", ...)` in `UserTeamRoleInspector\Plugin.cs`
   (sibling repo `../User Team Role Inspector with Matrix BU/UserTeamRoleInspector/Plugin.cs:9`),
   which is currently `"User/Team Role Inspector"`.
4. In `UserTeamRoleInspectorControl`, implement `OnIncomingMessage` to accept a
   `RoleAssignerHandoff`-shaped payload (via reflection/duck-typing on the `dynamic`, since
   there's no shared assembly) and pre-select/filter to that user or team.
5. Before committing to this, do a quick live smoke test with both tools installed in the
   same XrmToolBox instance to confirm: (a) the host actually launches/activates the named
   tool if it isn't already open, and (b) unresolved `TargetPlugin` names fail
   gracefully rather than throwing — neither behavior was confirmed from the source
   excerpts pulled in this research pass (fetches were AI-summarized excerpts of raw GitHub
   files, not the full `MainForm.cs` broker/routing implementation).

If a live test shows the host does *not* auto-launch a not-yet-open tool (only routes to
an already-open instance), the fallback is unchanged from today: no first-class way to
launch cold, so either prompt the user to open the tool manually first, or fall back to a
low-tech handoff — e.g. writing the selected team/user id to the clipboard or a small temp
JSON file for the user to paste/open in the other tool.

## Sources

- [MscrmTools/XrmToolBox](https://github.com/MscrmTools/XrmToolBox) —
  `XrmToolBox.Extensibility/Interfaces/IMessageBusHost.cs` (raw GitHub content, master)
- [MscrmTools/XrmToolBox](https://github.com/MscrmTools/XrmToolBox) —
  `XrmToolBox.Extensibility/Args/MessageBusEventArgs.cs` (raw GitHub content, master)
- [MscrmTools/XrmToolBox](https://github.com/MscrmTools/XrmToolBox) —
  `Plugins/MsCrmTools.SampleTool/SampleTool.cs` (raw GitHub content, master)
- [Integrate with FetchXML Builder – JonasR.app](https://jonasr.app/fxb/integrate/)
- [MscrmTools/XrmToolBox Wiki — Develop your own custom plugin for XrmToolBox](https://github.com/MscrmTools/XrmToolBox/wiki/Develop-your-own-custom-plugin-for-XrmToolBox)
- [MscrmTools/XrmToolBox Issue #1385 — "Way to implement nested controls?"](https://github.com/MscrmTools/XrmToolBox/issues/1385) —
  checked, confirmed **not** relevant to inter-tool opening (covers nested `UserControl`s
  within a single plugin only)
- Local: `BuMatrixSecurityRoleAssigner/Plugin.cs`
  (line 9, `ExportMetadata("Name", "BU Matrix Security Role Assigner")`)
- Local: `BuMatrixSecurityRoleAssigner/BuMatrixSecurityRoleAssignerControl.cs`
  (confirms current control derives from `PluginControlBase` only — no messaging interface
  implemented yet)
- Local: sibling repo `../User Team Role Inspector with Matrix BU/UserTeamRoleInspector/Plugin.cs`
  (line 9, `ExportMetadata("Name", "User/Team Role Inspector")`)
