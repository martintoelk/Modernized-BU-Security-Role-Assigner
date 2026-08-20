using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace TeamRoleManager
{
    // This is the entry point XrmToolBox discovers via MEF. It just hands back the UI control.
    [Export(typeof(IXrmToolBoxPlugin))]
    [ExportMetadata("Name", "Team Role Manager")]
    [ExportMetadata("Description", "Bulk-assign or remove security roles for one or more teams. Resolves each role to the correct copy in the team's business unit.")]
    [ExportMetadata("SmallImageBase64", null)]
    [ExportMetadata("BigImageBase64", null)]
    [ExportMetadata("BackgroundColor", "White")]
    [ExportMetadata("PrimaryFontColor", "Black")]
    [ExportMetadata("SecondaryFontColor", "DarkGray")]
    public class TeamRoleManagerPlugin : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl()
        {
            return new TeamRoleManagerControl();
        }
    }
}
