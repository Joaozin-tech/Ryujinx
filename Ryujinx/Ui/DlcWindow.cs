using Gtk;
using Ryujinx.HLE.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using GUI        = Gtk.Builder.ObjectAttribute;
using JsonHelper = Ryujinx.Common.Utilities.JsonHelper;

namespace Ryujinx.Ui
{
    public class DlcWindow : Window
    {
        private readonly string          _dlcJsonPath;
        private Dictionary<string, bool> _dlcDictionary;

#pragma warning disable CS0649, IDE0044
        [GUI] Label    _baseTitleInfoLabel;
        [GUI] TreeView _dlcTreeView;
#pragma warning restore CS0649, IDE0044

        public DlcWindow(string titleId, string titleName, VirtualFileSystem virtualFileSystem) : this(new Builder("Ryujinx.Ui.DlcWindow.glade"), titleId, titleName, virtualFileSystem) { }

        private DlcWindow(Builder builder, string titleId, string titleName, VirtualFileSystem virtualFileSystem) : base(builder.GetObject("_dlcWindow").Handle)
        {
            builder.Autoconnect(this);

            _dlcJsonPath             = System.IO.Path.Combine(virtualFileSystem.GetBasePath(), "games", titleId, "dlc.json");
            _baseTitleInfoLabel.Text = $"DLC Available for {titleName} [{titleId.ToUpper()}]";

            try
            {
                _dlcDictionary = JsonHelper.DeserializeFromFile<Dictionary<string, bool>>(_dlcJsonPath);
            }
            catch
            {
                _dlcDictionary = new Dictionary<string, bool>();
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

            foreach (KeyValuePair<string, bool> dlcData in _dlcDictionary)
            {
                ((ListStore)_dlcTreeView.Model).AppendValues(
                    dlcData.Value,
                    dlcData.Key);
            }
        }

        private void SaveButton_Clicked(object sender, EventArgs args)
        {
            _dlcDictionary.Clear();

            if (_dlcTreeView.Model.GetIterFirst(out TreeIter treeIter))
            {
                do
                {
                    _dlcDictionary.Add(_dlcTreeView.Model.GetValue(treeIter, 1).ToString(), (bool)_dlcTreeView.Model.GetValue(treeIter, 0));
                } 
                while (_dlcTreeView.Model.IterNext(ref treeIter));
            }

            File.WriteAllText(_dlcJsonPath, JsonSerializer.Serialize(_dlcDictionary));

            Dispose();
        }

        private void CancelButton_Clicked(object sender, EventArgs args)
        {
            Dispose();
        }
    }
}