
namespace MonoDevelop.Ide.Projects
{
	public partial class GtkProjectConfigurationWidget
	{
		private Gtk.HBox mainHBox;

		private Gtk.EventBox leftBorderEventBox;

		private Gtk.EventBox projectConfigurationTableEventBox;

		private Gtk.VBox projectConfigurationVBox;

		private Gtk.EventBox projectConfigurationTopEventBox;

		private Gtk.Table projectConfigurationTable;

		private Gtk.HBox versionControlLabelHBox;

		private Gtk.Label versionControlSpacerLabel;

		private Gtk.Label versionControlLabel;

		private Gtk.CheckButton useGitCheckBox;

		private Gtk.Entry solutionNameTextBox;

		private Gtk.DrawingArea solutionNameSeparator;

		private Gtk.Label solutionNameLabel;

		private Gtk.Entry projectNameTextBox;

		private Gtk.Label projectNameLabel;

		private Gtk.Entry locationTextBox;

		private Gtk.DrawingArea locationSeparator;

		private Gtk.Label locationLabel;

		private Gtk.CheckButton createProjectWithinSolutionDirectoryCheckBox;

		private Gtk.CheckButton createGitIgnoreFileCheckBox;

		private Gtk.Button browseButton;

		private Gtk.EventBox projectConfigurationBottomEventBox;

		private Gtk.EventBox projectConfigurationRightBorderEventBox;

		private Gtk.EventBox eventBox;

		private Gtk.VBox previewProjectFolderVBox;

		private MonoDevelop.Ide.Projects.GtkProjectFolderPreviewWidget projectFolderPreviewWidget;

		private void Build()
		{
		}
	}
}
