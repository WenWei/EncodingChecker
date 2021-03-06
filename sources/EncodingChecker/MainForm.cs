﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Deployment.Application;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using Ude;

namespace EncodingChecker
{
    public partial class MainForm : Form
    {
        private sealed class WorkerArgs
        {
            internal CurrentAction Action;
            internal string BaseDirectory;
            internal bool IncludeSubdirectories;
            internal string FileMasks;
            internal List<string> ValidCharsets;
        }

        private sealed class WorkerProgress
        {
            internal string FileName;
            internal string DirectoryName;
            internal string Charset;
        }

        private enum CurrentAction
        {
            View,
            Validate,
            Convert,
        }

        private readonly BackgroundWorker _actionWorker;
        private CurrentAction _currentAction;
        private Settings _settings;

        //https://msdn.microsoft.com/en-us/library/system.text.encoding(v=vs.110).aspx
        private Dictionary<string, string> _encodingNameMap = new Dictionary<string, string> {
            {"UTF-8","utf-8" },
            {"UTF-16LE","utf-16" },
            {"UTF-16BE","utf-16" },
            {"UTF-32BE","utf-32BE" },
            {"UTF-32LE","utf-32" },
            //{"X-ISO-10646-UCS-4-3412","" },
            //{"X-ISO-10646-UCS-4-2413","" },
            //{"windows-1251","" },
            //{"windows-1252","" },
            //{"windows-1253","" },
            //{"windows-1255","" },
            {"Big-5","big5" },
            {"EUC-KR","euc-kr" },
            {"EUC-JP","edu-jp" },
            //{"EUC-TW","" },
            { "gb18030","GB18030" },
            //{"ISO-2022-JP","" },
            //{"ISO-2022-CN","" },
            //{"ISO-2022-KR","" },
            {"HZ-GB-2312","hz-gb-2312" },
            //{"Shift-JIS","" },
            //{"x-mac-cyrillic","" },
            //{"KOI8-R","" },
            {"IBM855","IBM855" },
            //{"IBM866","" },
            //{"ISO-8859-2","" },
            //{"ISO-8859-5","" },
            //{"ISO-8859-7","" },
            //{"ISO-8859-8","" },
            //{"TIS620","" }
        };
        

        public MainForm()
        {
            InitializeComponent();

            _actionWorker = new BackgroundWorker();
            _actionWorker.WorkerReportsProgress = true;
            _actionWorker.WorkerSupportsCancellation = true;
            _actionWorker.DoWork += ActionWorkerDoWork;
            _actionWorker.ProgressChanged += ActionWorkerProgressChanged;
            _actionWorker.RunWorkerCompleted += ActionWorkerCompleted;
        }

        #region Form events
        private void OnFormLoad(object sender, EventArgs e)
        {
            IEnumerable<string> validCharsets = GetSupportedCharsets();
            foreach (string validCharset in validCharsets)
            {
                lstValidCharsets.Items.Add(validCharset);
                lstConvert.Items.Add(validCharset);
            }
            if (lstConvert.Items.Count > 0)
                lstConvert.SelectedIndex = 0;

            btnView.Tag = CurrentAction.View;
            btnValidate.Tag = CurrentAction.Validate;
            btnConvert.Tag = CurrentAction.Convert;

            LoadSettings();

            //Size the result list columns based on the initial size of the window
            lstResults.Columns[0].AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
            int remainingWidth = lstResults.Width - lstResults.Columns[0].Width;
            lstResults.Columns[1].Width = (30 * remainingWidth) / 100;
            lstResults.Columns[2].AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
        }

        private void OnBrowseDirectories(object sender, EventArgs e)
        {
            dlgBrowseDirectories.SelectedPath = lstBaseDirectory.Text;
            if (dlgBrowseDirectories.ShowDialog(this) == DialogResult.OK)
            {
                lstBaseDirectory.Text = dlgBrowseDirectories.SelectedPath;
                lstBaseDirectory.Items.Add(dlgBrowseDirectories.SelectedPath);
            }
        }

        private void OnSelectDeselectAll(object sender, EventArgs e)
        {
            lstResults.ItemChecked -= OnResultItemChecked;
            try
            {
                bool isChecked = chkSelectDeselectAll.Checked;
                foreach (ListViewItem item in lstResults.Items)
                    item.Checked = isChecked;
            }
            finally
            {
                lstResults.ItemChecked += OnResultItemChecked;
            }
        }

        private void OnResultItemChecked(object sender, ItemCheckedEventArgs e)
        {
            chkSelectDeselectAll.CheckedChanged -= OnSelectDeselectAll;
            try
            {
                if (lstResults.CheckedItems.Count == 0)
                    chkSelectDeselectAll.CheckState = CheckState.Unchecked;
                else if (lstResults.CheckedItems.Count == lstResults.Items.Count)
                    chkSelectDeselectAll.CheckState = CheckState.Checked;
                else
                    chkSelectDeselectAll.CheckState = CheckState.Indeterminate;
            }
            finally
            {
                chkSelectDeselectAll.CheckedChanged += OnSelectDeselectAll;
            }
        }

        private void OnHelp(object sender, EventArgs e)
        {
            ProcessStartInfo psi = new ProcessStartInfo("http://encodingchecker.codeplex.com/documentation");
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        private void OnAbout(object sender, EventArgs e)
        {
            using (AboutForm aboutForm = new AboutForm())
                aboutForm.ShowDialog(this);
        }
        #endregion

        #region Action button handling
        private void OnAction(object sender, EventArgs e)
        {
            Button actionButton = (Button)sender;
            CurrentAction action = (CurrentAction)actionButton.Tag;
            StartAction(action);
        }

        private void StartAction(CurrentAction action)
        {
            string directory = lstBaseDirectory.Text;
            if (string.IsNullOrEmpty(directory))
            {
                ShowWarning("Please specify a directory to check");
                return;
            }
            if (!Directory.Exists(directory))
            {
                ShowWarning("The directory you specified '{0}' does not exist", directory);
                return;
            }
            if (action == CurrentAction.Validate && lstValidCharsets.CheckedItems.Count == 0)
            {
                ShowWarning("Select one or more valid character sets to proceed with validation");
                return;
            }

            _currentAction = action;

            _settings.RecentDirectories.Add(directory);

            UpdateControlsOnActionStart(action);

            List<string> validCharsets = new List<string>(lstValidCharsets.CheckedItems.Count);
            foreach (string validCharset in lstValidCharsets.CheckedItems)
                validCharsets.Add(validCharset);

            WorkerArgs args = new WorkerArgs();
            args.Action = action;
            args.BaseDirectory = directory;
            args.IncludeSubdirectories = chkIncludeSubdirectories.Checked;
            args.FileMasks = txtFileMasks.Text;
            args.ValidCharsets = validCharsets;
            _actionWorker.RunWorkerAsync(args);
        }

        private void OnConvert(object sender, EventArgs e)
        {
            if (lstResults.CheckedItems.Count == 0)
            {
                ShowWarning("Select one or more files to convert");
                return;
            }

            foreach (ListViewItem item in lstResults.CheckedItems)
            {
                string charset = item.SubItems[ResultsColumnCharset].Text;
                if (charset == "(Unknown)")
                    charset = null;
                string fileName = item.SubItems[ResultsColumnFileName].Text;
                string directory = item.SubItems[ResultsColumnDirectory].Text;
                string filePath = Path.Combine(directory, fileName);

                FileAttributes attributes = File.GetAttributes(filePath);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    attributes = attributes ^ FileAttributes.ReadOnly;
                    File.SetAttributes(filePath, attributes);
                }

                string content;

                using (StreamReader reader = charset == null ? new StreamReader(filePath, true) : new StreamReader(filePath, Encoding.GetEncoding(charset)))
                    content = reader.ReadToEnd();

                

                string targetCharset = (string)lstConvert.SelectedItem;
                if(_encodingNameMap.ContainsKey(targetCharset))
                    targetCharset = _encodingNameMap[targetCharset];

                using (StreamWriter writer = new StreamWriter(filePath, false, Encoding.GetEncoding(targetCharset)))
                {
                    var contentConverted = Common.TranslateContent(content, GetTranslateOption());
                    writer.Write(contentConverted);
                    writer.Flush();
                }

                item.Checked = false;
                item.ImageIndex = 0;
                item.SubItems[ResultsColumnCharset].Text = targetCharset;
            }
        }

        private void OnCancelAction(object sender, EventArgs e)
        {
            if (_actionWorker.IsBusy)
            {
                btnCancel.Visible = false;
                _actionWorker.CancelAsync();
            }
        }
        #endregion

        #region Background worker event handlers and helper methods
        private static void ActionWorkerDoWork(object sender, DoWorkEventArgs e)
        {
            const int progressBufferSize = 5;

            BackgroundWorker worker = (BackgroundWorker)sender;
            WorkerArgs args = (WorkerArgs)e.Argument;

            string[] allFiles = Directory.GetFiles(args.BaseDirectory, "*.*",
                args.IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            WorkerProgress[] progressBuffer = new WorkerProgress[progressBufferSize];
            int reportBufferCounter = 1;

            IEnumerable<Regex> maskPatterns = GenerateMaskPatterns(args.FileMasks);
            for (int i = 0; i < allFiles.Length; i++)
            {
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }

                string path = allFiles[i];
                string fileName = Path.GetFileName(path);
                if (!SatisfiesMaskPatterns(fileName, maskPatterns))
                    continue;

                CharsetDetector detector = new CharsetDetector();
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    detector.Feed(fs);
                    detector.DataEnd();
                }
                if (args.Action == CurrentAction.Validate)
                {
                    if (detector.Charset == null)
                        continue;
                    if (args.ValidCharsets.Contains(detector.Charset))
                        continue;
                }

                string directoryName = Path.GetDirectoryName(path);

                WorkerProgress progress = new WorkerProgress();
                progress.Charset = detector.Charset ?? "(Unknown)";
                progress.FileName = fileName;
                progress.DirectoryName = directoryName;
                progressBuffer[reportBufferCounter - 1] = progress;
                reportBufferCounter++;
                //if (reportBufferCounter > progressBufferSize)
                {
                    reportBufferCounter = 1;
                    int percentageCompleted = (i * 100) / allFiles.Length;
                    WorkerProgress[] reportProgress = new WorkerProgress[progressBufferSize];
                    Array.Copy(progressBuffer, reportProgress, progressBufferSize);
                    worker.ReportProgress(percentageCompleted, reportProgress);
                    Array.Clear(progressBuffer, 0, progressBufferSize);
                }
            }
        }

        private static IEnumerable<Regex> GenerateMaskPatterns(string fileMaskString)
        {
            string[] fileMasks = fileMaskString.Split(new string[] { Environment.NewLine },
                StringSplitOptions.RemoveEmptyEntries);
            string[] processedFileMasks = Array.FindAll(fileMasks, delegate(string mask) { return mask.Trim().Length > 0; });
            if (processedFileMasks.Length == 0)
                processedFileMasks = new string[] { "*.*" };

            List<Regex> maskPatterns = new List<Regex>(processedFileMasks.Length);
            foreach (string fileMask in processedFileMasks)
            {
                if (string.IsNullOrEmpty(fileMask))
                    continue;
                Regex maskPattern =
                    new Regex("^" + fileMask.Replace(".", "[.]").Replace("*", ".*").Replace("?", ".") + "$",
                        RegexOptions.IgnoreCase);
                maskPatterns.Add(maskPattern);
            }
            return maskPatterns;
        }

        private static bool SatisfiesMaskPatterns(string fileName, IEnumerable<Regex> maskPatterns)
        {
            foreach (Regex maskPattern in maskPatterns)
            {
                if (maskPattern.IsMatch(fileName))
                    return true;
            }
            return false;
        }

        private void ActionWorkerProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            WorkerProgress[] progresses = (WorkerProgress[])e.UserState;

            foreach (WorkerProgress progress in progresses)
            {
                if (progress == null)
                    break;
                ListViewItem resultItem = new ListViewItem(new string[] { progress.Charset, progress.FileName, progress.DirectoryName }, -1);
                lstResults.Items.Add(resultItem);
                actionStatus.Text = progress.FileName;
            }

            actionProgress.Value = e.ProgressPercentage;
        }

        private void ActionWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (lstResults.Items.Count > 0)
            {
                foreach (ColumnHeader columnHeader in lstResults.Columns)
                    columnHeader.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
            }
            UpdateControlsOnActionDone();
        }
        #endregion

        #region Loading and saving of settings
        private void LoadSettings()
        {
            string settingsFileName = GetSettingsFileName();
            if (!File.Exists(settingsFileName))
                return;
            using (FileStream settingsFile = new FileStream(settingsFileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                object settingsInstance = formatter.Deserialize(settingsFile);
                _settings = (Settings)settingsInstance;
            }

            if (_settings.RecentDirectories != null && _settings.RecentDirectories.Count > 0)
            {
                foreach (string recentDirectory in _settings.RecentDirectories)
                    lstBaseDirectory.Items.Add(recentDirectory);
                lstBaseDirectory.SelectedIndex = 0;
            }
            else
                lstBaseDirectory.Text = Environment.CurrentDirectory;
            chkIncludeSubdirectories.Checked = _settings.IncludeSubdirectories;
            txtFileMasks.Text = _settings.FileMasks;
            if (_settings.ValidCharsets != null && _settings.ValidCharsets.Length > 0)
            {
                for (int i = 0; i < lstValidCharsets.Items.Count; i++)
                    if (Array.Exists(_settings.ValidCharsets, delegate(string charset) {
                                                                  return charset.Equals((string)lstValidCharsets.Items[i]);
                                                              }))
                        lstValidCharsets.SetItemChecked(i, true);
            }
            if (_settings.WindowPosition != null)
                _settings.WindowPosition.ApplyTo(this);
        }

        private void SaveSettings()
        {
            if (_settings == null)
                _settings = new Settings();
            _settings.IncludeSubdirectories = chkIncludeSubdirectories.Checked;
            _settings.FileMasks = txtFileMasks.Text;

            _settings.ValidCharsets = new string[lstValidCharsets.CheckedItems.Count];
            for (int i = 0; i < lstValidCharsets.CheckedItems.Count; i++)
                _settings.ValidCharsets[i] = (string)lstValidCharsets.CheckedItems[i];

            _settings.WindowPosition = new WindowPosition();
            _settings.WindowPosition.Left = Left;
            _settings.WindowPosition.Top = Top;
            _settings.WindowPosition.Width = Width;
            _settings.WindowPosition.Height = Height;

            string settingsFileName = GetSettingsFileName();
            using (
                FileStream settingsFile = new FileStream(settingsFileName, FileMode.Create, FileAccess.Write,
                    FileShare.None))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(settingsFile, _settings);
                settingsFile.Flush();
            }
        }

        private static string GetSettingsFileName()
        {
            string dataDirectory = ApplicationDeployment.IsNetworkDeployed
                ? ApplicationDeployment.CurrentDeployment.DataDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(dataDirectory) || !Directory.Exists(dataDirectory))
                dataDirectory = Environment.CurrentDirectory;
            dataDirectory = Path.Combine(dataDirectory, "EncodingChecker");
            if (!Directory.Exists(dataDirectory))
                Directory.CreateDirectory(dataDirectory);
            return Path.Combine(dataDirectory, "Settings.bin");
        }
        #endregion

        private void UpdateControlsOnActionStart(CurrentAction action)
        {
            btnView.Enabled = false;
            btnValidate.Enabled = false;

            lblConvert.Enabled = false;
            lstConvert.Enabled = false;
            btnConvert.Enabled = false;
            chkSelectDeselectAll.Enabled = false;

            btnCancel.Visible = true;

            lstResults.Items.Clear();

            actionProgress.Value = 0;
            actionProgress.Visible = true;
            actionStatus.Text = string.Empty;
        }

        private void UpdateControlsOnActionDone()
        {
            btnView.Enabled = true;
            btnValidate.Enabled = true;

            if (lstResults.Items.Count > 0)
            {
                lblConvert.Enabled = true;
                lstConvert.Enabled = true;
                btnConvert.Enabled = true;
                chkSelectDeselectAll.Enabled = true;

                if (lstValidCharsets.CheckedItems.Count > 0)
                {
                    string firstValidCharset = (string)lstValidCharsets.CheckedItems[0];
                    for (int i = 0; i < lstConvert.Items.Count; i++)
                    {
                        string convertCharset = (string)lstConvert.Items[i];
                        if (firstValidCharset.Equals(convertCharset, StringComparison.OrdinalIgnoreCase))
                        {
                            lstConvert.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }

            btnCancel.Visible = false;

            actionProgress.Visible = false;

            string statusMessage = _currentAction == CurrentAction.View
                ? "{0} files processed" : "{0} files do not have the correct encoding";
            actionStatus.Text = string.Format(statusMessage, lstResults.Items.Count);
        }

        private static IEnumerable<string> GetSupportedCharsets()
        {
            //Using reflection, figure out all the charsets that the Ude framework supports by reflecting
            //over all the strings constants in the Ude.Charsets class. These represent all the encodings
            //that can be detected by the program.
            FieldInfo[] charsetConstants = typeof(Charsets).GetFields(BindingFlags.GetField | BindingFlags.Static | BindingFlags.Public);
            foreach (FieldInfo charsetConstant in charsetConstants)
            {
                if (charsetConstant.FieldType == typeof(string))
                    yield return (string)charsetConstant.GetValue(null);
            }
        }

        private void ShowWarning(string message, params object[] args)
        {
            MessageBox.Show(this, string.Format(message, args), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private const int ResultsColumnCharset = 0;
        private const int ResultsColumnFileName = 1;
        private const int ResultsColumnDirectory = 2;

        private void toolStripMenuItemPreview_Click(object sender, EventArgs e)
        {
            foreach(ListViewItem item in lstResults.SelectedItems)
            {
                string charset = item.SubItems[ResultsColumnCharset].Text;
                if (charset == "(Unknown)")
                    charset = null;

                if(_encodingNameMap.ContainsKey(charset)) charset = _encodingNameMap[charset];

                string fileName = item.SubItems[ResultsColumnFileName].Text;
                string directory = item.SubItems[ResultsColumnDirectory].Text;
                string filePath = Path.Combine(directory, fileName);

                FileAttributes attributes = File.GetAttributes(filePath);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    attributes = attributes ^ FileAttributes.ReadOnly;
                    File.SetAttributes(filePath, attributes);
                }

                string content=null;

                using (StreamReader reader = charset == null ? new StreamReader(filePath, true) : new StreamReader(filePath, Encoding.GetEncoding(charset)))
                    content = reader.ReadToEnd();

                string targetCharset = (string)lstConvert.SelectedItem;

                var f = new PreviewForm(content, charset, targetCharset, GetTranslateOption(), filePath);
                f.Show();

            }
        }


        private void openGb18030ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowContent("GB18030");
        }

        private void openUTF8ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowContent("utf-8");
        }

        private void openUTF16ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowContent("utf-16");
        }

        private void openUTF32ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowContent("utf-32");
        }

        private void openBIG5ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowContent("big5");
        }


        private Translate GetTranslateOption()
        {
            Translate translate = Translate.None;
            if(radioButtonToTraditional.Checked) translate = Translate.ToTraditional;
            else if(radioButtonToSimplified.Checked) translate = Translate.ToSimplified;
            return translate;
        }

        private void ShowContent(string charset)
        {
            foreach(ListViewItem item in lstResults.SelectedItems)
            {
                string fileName = item.SubItems[ResultsColumnFileName].Text;
                string directory = item.SubItems[ResultsColumnDirectory].Text;
                string filePath = Path.Combine(directory, fileName);

                string content=null;

                using (StreamReader reader = charset == null ? new StreamReader(filePath, true) : new StreamReader(filePath, Encoding.GetEncoding(charset)))
                    content = reader.ReadToEnd();


                string targetCharset = (string)lstConvert.SelectedItem;

                var f = new PreviewForm(content, charset, targetCharset, GetTranslateOption(), filePath);
                f.Show();
            }
        }


    }
}