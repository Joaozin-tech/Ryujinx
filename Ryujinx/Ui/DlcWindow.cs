using Gtk;
using LibHac;
using LibHac.Common;
using LibHac.FsSystem.NcaUtils;
using LibHac.Fs;
using LibHac.FsSystem;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;

using GUI        = Gtk.Builder.ObjectAttribute;
using JsonHelper = Ryujinx.Common.Utilities.JsonHelper;

namespace Ryujinx.Ui
{
    public class DlcWindow : Window
    {
        private readonly VirtualFileSystem _virtualFileSystem;
        private readonly string            _titleId;
        private readonly string            _dlcJsonPath;
        private List<DlcMetadata>          _dlcWindowData;

#pragma warning disable CS0649, IDE0044
        [GUI] Label         _baseTitleInfoLabel;
        [GUI] TreeView      _dlcTreeView;
        [GUI] TreeSelection _dlcTreeSelection;
#pragma warning restore CS0649, IDE0044

        public DlcWindow(string titleId, string titleName, VirtualFileSystem virtualFileSystem) : this(new Builder("Ryujinx.Ui.DlcWindow.glade"), titleId, titleName, virtualFileSystem) { }

        private DlcWindow(Builder builder, string titleId, string titleName, VirtualFileSystem virtualFileSystem) : base(builder.GetObject("_dlcWindow").Handle)
        {
            builder.Autoconnect(this);

            _titleId                 = titleId;
            _virtualFileSystem       = virtualFileSystem;
            _dlcJsonPath             = System.IO.Path.Combine(_virtualFileSystem.GetBasePath(), "games", _titleId, "dlc.json");
            _baseTitleInfoLabel.Text = $"DLC Available for {titleName} [{titleId.ToUpper()}]";

            try
            {
                _dlcWindowData = JsonHelper.DeserializeFromFile<List<DlcMetadata>>(_dlcJsonPath);
            }
            catch
            {
                _dlcWindowData = new List<DlcMetadata>();
            }

            _dlcTreeView.Model = new ListStore(
                typeof(bool),
                typeof(string));

            CellRendererToggle enableToggle = new CellRendererToggle();
            enableToggle.Toggled += (sender, args) =>
            {
                _dlcTreeView.Model.GetIter(out TreeIter treeIter, new TreePath(args.Path));
                _dlcTreeView.Model.SetValue(treeIter, 0, !(bool)_dlcTreeView.Model.GetValue(treeIter, 0));
            };

            _dlcTreeView.AppendColumn("Enabled", enableToggle,           "active", 0);
            _dlcTreeView.AppendColumn("Path",    new CellRendererText(), "text",   1);

            foreach (DlcMetadata dlcMetadata in _dlcWindowData)
            {
                AddDlc(dlcMetadata, false);
            }
        }

        private void AddDlc(DlcMetadata dlcMetadata, bool showErrorDialog = true)
        {
            if (File.Exists(dlcMetadata.Path))
            {
                using (FileStream file = new FileStream(dlcMetadata.Path, FileMode.Open, FileAccess.Read))
                {
                    PartitionFileSystem nsp = new PartitionFileSystem(file.AsStorage());

                    _virtualFileSystem.ImportTickets(nsp);

                    foreach (DirectoryEntryEx fileEntry in nsp.EnumerateEntries("/", "*.nca"))
                    {
                        nsp.OpenFile(out IFile ncaFile, fileEntry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();

                        try
                        {
                            Nca nca = new Nca(_virtualFileSystem.KeySet, ncaFile.AsStorage());

                            if (nca.Header.ContentType == NcaContentType.PublicData)
                            {
                                string baseTitleId = $"{nca.Header.TitleId.ToString("x16").Substring(0, 12)}{GetPreviousHexDigit(nca.Header.TitleId.ToString("x16").Substring(12, 1))}000";

                                if (baseTitleId != _titleId)
                                {
                                    if (showErrorDialog)
                                    {
                                        GtkDialog.CreateErrorDialog("The specified file does not contain a DLC for the selected title!");
                                    }

                                    break;
                                }

                                ((ListStore)_dlcTreeView.Model).AppendValues(
                                    dlcMetadata.Enabled,
                                    dlcMetadata.Path);
                            }
                        }
                        catch (InvalidDataException exception)
                        {
                            Logger.PrintError(LogClass.Application, $"{exception.Message}. Errored File: {dlcMetadata.Path}");

                            if (showErrorDialog)
                            {
                                GtkDialog.CreateInfoDialog("Ryujinx - Error", "Add DLC Failed!", "The NCA header content type check has failed. This is usually because the header key is incorrect or missing.");
                            }

                            break;
                        }
                        catch (MissingKeyException exception)
                        {
                            Logger.PrintError(LogClass.Application, $"Your key set is missing a key with the name: {exception.Name}. Errored File: {dlcMetadata.Path}");

                            if (showErrorDialog)
                            {
                                GtkDialog.CreateInfoDialog("Ryujinx - Error", "Add DLC Failed!", $"Your key set is missing a key with the name: {exception.Name}");
                            }

                            break;
                        }
                    }
                }
            }
        }

        private void AddButton_Clicked(object sender, EventArgs args)
        {
            FileChooserDialog fileChooser = new FileChooserDialog("Select DLC files", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "Add", ResponseType.Accept)
            {
                SelectMultiple = true,
                Filter         = new FileFilter()
            };
            fileChooser.SetPosition(WindowPosition.Center);
            fileChooser.Filter.AddPattern("*.nsp");

            if (fileChooser.Run() == (int)ResponseType.Accept)
            {
                foreach (string path in fileChooser.Filenames)
                {
                    AddDlc(new DlcMetadata
                    {
                        Path    = path,
                        Enabled = true
                    });
                }
            }

            fileChooser.Dispose();
        }

        private void RemoveButton_Clicked(object sender, EventArgs args)
        {
            if (_dlcTreeSelection.GetSelected(out ITreeModel treeModel, out TreeIter treeIter))
            {
                ((ListStore)treeModel).Remove(ref treeIter);
            }
        }

        private void SaveButton_Clicked(object sender, EventArgs args)
        {
            _dlcWindowData.Clear();
            foreach (DlcMetadata dlcMetadata in GetDlcListFromListStore(_dlcTreeView.Model))
            {
                _dlcWindowData.Add(dlcMetadata);
            }

            File.WriteAllText(_dlcJsonPath, JsonHelper.Serialize(_dlcWindowData, true));

            Dispose();
        }

        private void CancelButton_Clicked(object sender, EventArgs args)
        {
            Dispose();
        }

        private static List<DlcMetadata> GetDlcListFromListStore(ITreeModel treeModel)
        {
            List<DlcMetadata> dlcList = new List<DlcMetadata>();

            if (treeModel.GetIterFirst(out TreeIter treeIter))
            {
                do
                {
                    dlcList.Add(new DlcMetadata
                    {
                        Path    = treeModel.GetValue(treeIter, 1).ToString(),
                        Enabled = (bool)treeModel.GetValue(treeIter, 0)
                    });
                } while (treeModel.IterNext(ref treeIter));
            }

            return dlcList;
        }

        public static string GetPreviousHexDigit(string hexDigit)
        {
            string[] hexDigits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "a", "b", "c", "d", "e", "f" };
            int i = Array.IndexOf(hexDigits, hexDigit) - 1;

            if (i < 0)
                i = 0;

            return hexDigits[i];
        }
    }
}