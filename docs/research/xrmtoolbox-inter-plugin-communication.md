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

Namespace: `XrmToolBox.Extensibility` — *not* `XrmToolBox.Extensibility.Args`, despite the file
path below; verified by reflection against `XrmToolBox.Extensibility.dll` 1.2025.10.74, whose
`Args` namespace holds only `DuplicateToolArgs`, `DuplicateToolWithConnectionArgs` and
`StatusBarMessageEventArgs`. A plugin control that already imports `XrmToolBox.Extensibility`
needs no extra `using` for it.
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

## Confirmed: the host cold-launches the target, and fails gracefully if it can't

The first version of this note left one question open — whether the host launches a target tool
that isn't already open, or only routes to an already-running instance — because it couldn't find
`MainForm` in the public repo. The broker is there, just not under that name: it lives in
`XrmToolBox/New/NewForm.cs`, and it answers the question outright.

```csharp
private void MainForm_MessageBroker(object sender, MessageBusEventArgs message)
{
    if (!IsMessageValid(sender, message)) return;

    var sourceDetail = ((PluginControlBase)sender).ConnectionDetail;

    var content = message.NewInstance ? null : GetPluginByName(message.TargetPlugin, sourceDetail?.ConnectionId ?? Guid.Empty);
    if (content == null)
    {
        pluginsForm.OpenPlugin(message.TargetPlugin);            // <- cold launch
    }

    content = GetPluginByName(message.TargetPlugin, sourceDetail?.ConnectionId ?? Guid.Empty);
    if (content == null)
    {
        MessageBox.Show($@"Cannot switch to tool {message.TargetPlugin}.", message.SourcePlugin, ...);
        return;
    }
    content.Show(dpMain, content.DockState);
    content.SendIncomingBrokerMessage(message);                  // -> target's OnIncomingMessage
}
```

What it settles:

- **Cold launch: yes.** With no matching instance open, the host calls
  `pluginsForm.OpenPlugin(targetPlugin)` and then re-resolves, so the tool is started, shown and
  handed the message. A sender never has to ask the user to open the target first.
- **Not installed: graceful, and the host reports it.** `OpenPlugin` shows *"Tool '<name>' was not
  found. You can install it from the Tool Library"*, then the still-null lookup produces *"Cannot
  switch to tool <name>."*, attributed to the **source** plugin. No exception reaches the sender,
  so a sender needs no "is it installed" pre-check — and shouldn't grow one, since it could only
  go out of date.
- **Instances are matched per connection.** `GetPluginByName(name, connectionId)` scopes the
  lookup to the source plugin's own `ConnectionId`, so the target opens against the environment
  the sender is connected to rather than reusing a window on a different one.
- **`SourcePlugin` is filled in for you.** `IsMessageValid` sets it from the sending plugin's form
  name when it's empty, and drops messages whose sender isn't an XrmToolBox plugin control hosted
  in a `PluginForm`.
- **Delivery is immediate, not "once the target is ready".** `SendIncomingBrokerMessage` calls the
  target's `OnIncomingMessage` synchronously, right after `Show(...)`. On a cold launch that lands
  on a control that may not yet have a connection, let alone any loaded data — so **a receiver
  must stash the payload and apply it once it can**, rather than assume `Service` and its caches
  are ready. Both tools here do that; see `RoleHandoff` and `ApplyHandoff`.


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
  actually installed in the user's XrmToolBox (via the Tool Library or manual copy). If it
  isn't, the host says so itself — see the confirmed-behavior section above — but there is no
  compile-time guarantee the target exists, and the CLAUDE.md-documented plugin isolation model
  (MEF discovery, host supplies all XTB/SDK assemblies, no shared state between plugin DLLs)
  means there never will be.
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
5. ~~Before committing to this, do a quick live smoke test~~ — **answered from the host's own
   source instead**, see "Confirmed: the host cold-launches the target" above. Both behaviors
   hold, so the clipboard / temp-file fallback this note used to hedge on was never needed and
   was never built.

**Status: implemented** (issue #17) — with one change of shape from the sketch above. The payload
is *not* a `RoleAssignerHandoff` object: `TargetArgument` may be `dynamic`, but the two tools are
separately built assemblies that cannot name each other's types, so duck-typing a POCO across that
boundary would have been reflection held together by a naming coincidence. It is a **string**
instead — the same reason FetchXML Builder's published contract is a string — with a documented
format and its own parser on each side, each pinned by tests against the same literal payloads.
See `BuMatrixSecurityRoleAssigner.Core/RoleHandoff.cs` here and
`UserTeamRoleInspector.Core/RoleHandoff.cs` in the sibling repo.

## Sources

- [MscrmTools/XrmToolBox](https://github.com/MscrmTools/XrmToolBox) —
  `XrmToolBox.Extensibility/Interfaces/IMessageBusHost.cs` (raw GitHub content, master)
- [MscrmTools/XrmToolBox](https://github.com/MscrmTools/XrmToolBox) —
  `XrmToolBox.Extensibility/Args/MessageBusEventArgs.cs` (raw GitHub content, master)
- [MscrmTools/XrmToolBox](https://github.com/MscrmTools/XrmToolBox) —
  `Plugins/MsCrmTools.SampleTool/SampleTool.cs` (raw GitHub content, master)
- [MscrmTools/XrmToolBox](https://github.com/MscrmTools/XrmToolBox) —
  `XrmToolBox/New/NewForm.cs` (the message broker; GitHub contents API, master), plus
  `XrmToolBox/New/PluginsForm.cs` and `XrmToolBox/New/PluginForm.cs` for `OpenPlugin` and
  `SendIncomingBrokerMessage`. Note there is no `MainForm.cs` in the repo — the host form is
  `NewForm`, which is why an earlier search for the broker came up empty.
- [Integrate with FetchXML Builder – JonasR.app](https://jonasr.app/fxb/integrate/)
- [MscrmTools/XrmToolBox Wiki — Develop your own custom plugin for XrmToolBox](https://github.com/MscrmTools/XrmToolBox/wiki/Develop-your-own-custom-plugin-for-XrmToolBox)
- [MscrmTools/XrmToolBox Issue #1385 — "Way to implement nested controls?"](https://github.com/MscrmTools/XrmToolBox/issues/1385) —
  checked, confirmed **not** relevant to inter-tool opening (covers nested `UserControl`s
  within a single plugin only)
- Local: `BuMatrixSecurityRoleAssigner/Plugin.cs`
  (line 9, `ExportMetadata("Name", "BU Matrix Security Role Assigner")`)
- Local: `BuMatrixSecurityRoleAssigner/BuMatrixSecurityRoleAssignerControl.cs`
  (now `PluginControlBase, IMessageBusHost` — it derived from `PluginControlBase` alone when
  this note was first written)
- Local: sibling repo `../User Team Role Inspector with Matrix BU/UserTeamRoleInspector/Plugin.cs`
  (line 9, `ExportMetadata("Name", "User/Team Role Inspector")`)
