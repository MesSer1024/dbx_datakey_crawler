using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Search_DBX_files
{
    public partial class MainWindow : Window
    {
        private Dictionary<String, List<FileInfo>> _resultingFiles;
        private Dictionary<String, List<String>> _resultingLines;
        private List<DiceItem> _items;
        private List<DiceItem> _suspects;

        private FileInfo[] _allDbxFiles;
        private FileInfo[] _allCppFiles;
        private long _fileId;
        private LoadedFiles _loader;
        private System.Timers.Timer _timer;
        private DateTime _timestamp;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                hideLoading();
                settingsControl.onAnalyzeButton += settingsControl_onAnalyzeButton;
                settingsControl.onLoadButton += settingsControl_onLoadButton;
                resultGrid.Visibility = Visibility.Collapsed;
                settingsControl.loadSettings("./config.ini");
            }
            catch (Exception e)
            {
                MessageBox.Show(String.Format("Error during initialization \n (most likely your config.ini-file is corrupt and you will have to solve this manually):\n\n {0}", e.ToString()));
            }
        }

        void settingsControl_onLoadButton()
        {
            onLoad(null, null);
        }

        void settingsControl_onAnalyzeButton()
        {
            onAnalyze(null, null);
        }

        private void onAnalyze(object sender, RoutedEventArgs e)
        {
            _items = new List<DiceItem>();
            _items.Clear();
            identifiersList.ItemsSource = _items;
            _allDbxFiles = new FileInfo[] { };
            _allCppFiles = new FileInfo[] { };
            _resultingFiles = new Dictionary<String, List<FileInfo>>();
            _resultingLines = new Dictionary<String, List<String>>();
            _timer = new System.Timers.Timer(125);
            int maxThreads = settingsControl.MaxThreads;

            var defFile = new FileInfo(settingsControl.DataDefinesFile);
            var dbxPath = new DirectoryInfo(settingsControl.DbxRoot);
            var cppPath = new DirectoryInfo(settingsControl.CppRoot);
            var cppFilter = settingsControl.CppLineFilter;
            var dbxFilter = settingsControl.DbxLineFilter;
            fileStatus.Content = "";
            guidStatus.Content = "";

            if (!validatePaths(defFile, dbxPath, cppPath))
            {
                settingsControl.setEnabled(true);
                MessageBox.Show("Invalid path to datadefines, dbx-folder or cpp-folder");
            }
            else
            {
                _timestamp = DateTime.Now;
                settingsControl.setEnabled(false);
                showLoading();
                updateThePopulationText("Populating files [Finding all DBX & CPP files in subfolders]");
                new System.Threading.Timer(obj =>
                {   
                    populateIdentifiers(defFile);
                    _allDbxFiles = dbxPath.GetFiles("*.dbx", SearchOption.AllDirectories);
                    updateThePopulationText("Populating files [Finding all DBX & CPP files in subfolders] \n DBX-files found - searching cpp-files...");
                    _allCppFiles = cppPath.GetFiles("*.cpp", SearchOption.AllDirectories);
                    this.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        blocker2.Content = "DBX&CPP Files populated, searching in files";
                        identifiersList.ItemsSource = _items;
                        identifiersList.Items.Refresh();
                    }));

                    _loader = new LoadedFiles(_allDbxFiles, _allCppFiles, dbxFilter, cppFilter, _items.AsReadOnly(), maxThreads);
                    _loader.OnComplete = handleAllDataAvailable;
                    _timer.Elapsed += _timer_Elapsed;
                    _timer.Start();
                    
                }, null, 2, System.Threading.Timeout.Infinite);
            }
        }

        void _timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            this.Dispatcher.Invoke((Action)(() =>
            {
                fileStatus.Content = String.Format("Files completed: {0}/{1}", _loader.TotalFiles - _loader.FilesLeft, _loader.TotalFiles);
                guidStatus.Content = "";
                blocker2.Content = String.Format("Hang on, I have approximately {0} lines to go through or so ...", 1477 * _loader.FilesLeft);
            }));
        }

        private void updateThePopulationText(string s)
        {
            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                blocker2.Content = s;
            }));
        }

        private void populateIdentifiers(FileInfo defFile)
        {
            var errors = new StringBuilder();

            using (var sr = new StreamReader(defFile.OpenRead()))
            {
                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine();
                    //only work on valid lines
                    if (line.StartsWith("#define"))
                    {
                        var comps = line.Split(new Char[] { ' ', '\t' });
                        if (comps.Length <= 3)
                        {
                            errors.AppendLine("Error: " + line);
                        }
                        else
                        {
                            var id = comps[1];
                            var guid = "";
                            for (int i = 3; i < comps.Length; ++i)
                            {
                                var s = comps[i].Trim();
                                if (s.Length > 0)
                                {
                                    guid = s;
                                }
                            }

                            if (id.Length <= 5 || guid == "")
                            {
                                errors.AppendLine("Could not find valid ID and Guid from following line: " + line);
                            }
                            else
                            {
                                var item = new DiceItem {Identifier = id, Guid = guid};
                                _items.Add(item);
                            }
                        }
                    }
                }
            }

            if (errors.Length > 0)
            {
                MessageBox.Show(errors.ToString());
            }
        }

        private void handleAllDataAvailable()
        {
            Console.WriteLine("Files analyzed in {0}ms", (DateTime.Now - _timestamp).TotalMilliseconds);
            _timer.Stop();
            _resultingFiles = _loader.FileResult;
            _resultingLines = _loader.LineResult;
            _loader = null;
            populateSuspects();
            outputToFile();
            updateIdentifiers();
            saveAsJson();
            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                hideLoading();
                onFilterChanged(null, null);
                settingsControl.setEnabled(true);
                Console.WriteLine("From start to finish {0}ms", (DateTime.Now - _timestamp).TotalMilliseconds);
            }));
        }

        private void populateSuspects()
        {
            _suspects = new List<DiceItem>();

            foreach (var item in _items)
            {
                if (_resultingLines.ContainsKey(item.Guid))
                {
                    var resultingLines = _resultingLines[item.Guid];
                    var filters = resultingLines.FindAll(a => a.IndexOf("setData", StringComparison.Ordinal) >= 0);
                    if (resultingLines.Count == filters.Count)
                    {
                        _suspects.Add(item);
                    }
                }
            }
        }

        private void outputToFile()
        {
            Console.WriteLine("!outputting to file!");
            StringBuilder sb = new StringBuilder();
            StringBuilder unusedSB = new StringBuilder();
            StringBuilder usedLines = new StringBuilder();
            StringBuilder suspects = new StringBuilder();
            foreach (var item in _suspects)
            {
                var guid = item.Guid;
                suspects.AppendFormat(Environment.NewLine + "{0} - {1}", item.Guid, item.Identifier);
                for (int i = 0; i < _resultingLines[guid].Count; ++i)
                {
                    var line = _resultingLines[guid][i];
                    var file = _resultingFiles[guid][i].Name;
                    suspects.AppendFormat(Environment.NewLine + "\t{0} | {1}", line, file);
                }
            }

            for (int i = 0; i < _items.Count; ++i)
            {
                var guid = _items[i].Guid;
                var id = _items[i].Identifier;
                if (_resultingFiles.ContainsKey(guid))
                {
                    //has references
                    var files = _resultingFiles[guid];
                    sb.AppendFormat(Environment.NewLine + "{0} - {1}", guid, id);
                    foreach (var file in files)
                    {
                        sb.Append(Environment.NewLine + "\t" + file);
                    }

                    //all lines
                    var lines = _resultingLines[guid];
                    usedLines.AppendFormat(Environment.NewLine + "{0} - {1}", guid, id);
                    foreach (var line in lines)
                    {
                        usedLines.Append(Environment.NewLine + "\t" + line);
                    }
                }
                else
                {
                    //no references exist
                    unusedSB.AppendFormat(Environment.NewLine + "{0} - {1}", guid, id);
                }
            }
            _fileId = DateTime.Now.Ticks;
            var di = new DirectoryInfo("./");
            di.CreateSubdirectory("./output/");
            var f = "./output/" + _fileId;

            using (var sw = new StreamWriter(f + "_usedKeys.txt", false))
            {
                sw.Write(sb.ToString());
                sw.Flush();

                FileInfo foo = new FileInfo(f + "_usedKeys.txt");
                foo.CopyTo("./output/__usedKeys.txt", true);
            }

            using (var sw = new StreamWriter(f + "_unusedKeys.txt", false))
            {
                sw.Write(unusedSB.ToString());
                sw.Flush();

                FileInfo foo = new FileInfo(f + "_unusedKeys.txt");
                foo.CopyTo("./output/__unusedKeys.txt", true);
            }

            using (var sw = new StreamWriter(f + "_usedLines.txt", false))
            {
                sw.Write(usedLines.ToString());
                sw.Flush();

                FileInfo foo = new FileInfo(f + "_usedLines.txt");
                foo.CopyTo("./output/__usedLines.txt", true);
            }

            using (var sw = new StreamWriter(f + "_suspects.txt", false))
            {
                sw.Write(suspects.ToString());
                sw.Flush();

                FileInfo foo = new FileInfo(f + "_suspects.txt");
                foo.CopyTo("./output/__suspects.txt", true);
            }
        }

        private void saveAsJson()
        {
            var file = String.Format("./output/{0}.dcs", _fileId);

            var save = new SavedData();
            save.Items = _items;
            save.Files = _resultingFiles;
            save.Lines = _resultingLines;
            var output = JsonConvert.SerializeObject(save);
            using (var sw = new StreamWriter(file, false))
            {
                sw.Write(output);
                sw.Flush();
            }

            using (var sw = new StreamWriter(String.Format("./output/_lastSave.dcs", _fileId), false))
            {
                sw.Write(output);
                sw.Flush();
            }
        }

        private void updateIdentifiers()
        {
            for (var i = 0; i < _items.Count; ++i)
            {
                var item = _items[i];

                var guid = item.Guid;
                if (_resultingFiles.ContainsKey(guid))
                {
                    item.ReferenceCount = _resultingFiles[guid].Count;
                    item.VisibleName = "[" + _resultingFiles[guid].Count.ToString("000") + "] " + item.Identifier;
                }
                else
                {
                    item.ReferenceCount = 0;
                    item.VisibleName = "[---] " + item.Identifier;
                }
            }
        }

        private void showLoading()
        {
            settingsControl.EnableAnalyzeButton = false;
            settingsControl.EnableLoadButton = false;
            blocker1.Visibility = Visibility.Visible;
            blocker2.Visibility = Visibility.Visible;
            fileStatus.Visibility = Visibility.Visible;
            guidStatus.Visibility = Visibility.Visible;
            identifiersList.IsEnabled = false;
        }

        private void hideLoading()
        {
            settingsControl.EnableAnalyzeButton = true;
            settingsControl.EnableLoadButton = true;
            blocker1.Visibility = Visibility.Hidden;
            blocker2.Visibility = Visibility.Hidden;
            fileStatus.Visibility = Visibility.Hidden;
            guidStatus.Visibility = Visibility.Hidden;
            identifiersList.IsEnabled = true;
            resultGrid.Visibility = Visibility.Visible;
        }

        private void insertUsage(string guid, FileInfo file)
        {
            if (!_resultingFiles.ContainsKey(guid))
            {
                _resultingFiles.Add(guid, new List<FileInfo>());
            }
            _resultingFiles[guid].Add(file);
        }

        private void insertLine(string guid, String line, int lineNumber = -1)
        {
            if (!_resultingLines.ContainsKey(guid))
            {
                _resultingLines.Add(guid, new List<String>());
            }
            var s = line.Trim();
            if (lineNumber >= 0)
            {
                s = "[" + lineNumber + "] " + s;
            }
            _resultingLines[guid].Add(s);
        }

        private bool validatePaths(FileInfo defFile, DirectoryInfo dbxPath, DirectoryInfo cppPath)
        {
            if (!defFile.Exists || !dbxPath.Exists || !cppPath.Exists)
                return false;
            return true;
        }

        private void onGuidSelected(object sender, SelectionChangedEventArgs e)
        {
            var item = (DiceItem)identifiersList.SelectedItem;
            if (item == null)
            {
                usedFileList.ItemsSource = null;
                usedLinesList.ItemsSource = null;
                return;
            }

            var guid = item.Guid;
            if (_resultingFiles.ContainsKey(guid))
            {
                var files = _resultingFiles[guid];
                var lines = _resultingLines[guid];
                usedFileList.ItemsSource = files;
                usedLinesList.ItemsSource = lines;
            }
            else
            {
                usedFileList.ItemsSource = null;
                usedLinesList.ItemsSource = null;
            }
        }

        private void onFileSelected(object sender, SelectionChangedEventArgs e)
        {
            var id = usedFileList.SelectedIndex;
            usedLinesList.SelectedIndex = id;
        }

        private void onLineSelected(object sender, SelectionChangedEventArgs e)
        {
            var id = usedLinesList.SelectedIndex;
            usedFileList.SelectedIndex = id;
        }

        private void onFilterChanged(object sender, RoutedEventArgs e)
        {
            if (_items == null)
            {
                return;
            }
            var filter = idFilter.Text;
            if (filter != "")
            {
                int foo = 0;
                if (int.TryParse(filter, out foo))
                {
                    identifiersList.ItemsSource = _items.FindAll(a => a.Guid.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                else
                {
                    identifiersList.ItemsSource = _items.FindAll(a => a.VisibleName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }
            else
            {
                identifiersList.ItemsSource = _items;
            }
            identifiersList.Items.Refresh();
        }

        private void onLoad(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "Datacrawler Save (*.dcs)|*.dcs";
            dlg.Multiselect = false;
            dlg.FileOk += dlg_FileOk;
            dlg.InitialDirectory = Environment.CurrentDirectory + "\\output\\";
            dlg.FileName = "_lastSave.dcs";
            dlg.Title = "Selected a previous search";
            dlg.ShowDialog();
        }

        void dlg_FileOk(object sender, CancelEventArgs e)
        {
            var dlg = sender as OpenFileDialog;
            doLoadFile(dlg.FileName);
        }

        private void doLoadFile(string filePath)
        {
            var file = new FileInfo(filePath);
            if (file.Exists)
            {
                showLoading();
                using (var sr = new StreamReader(file.FullName))
                {
                    string s = sr.ReadToEnd();
                    var load = JsonConvert.DeserializeObject<SavedData>(s);
                    _items = load.Items;
                    _resultingFiles = load.Files;
                    _resultingLines = load.Lines;
                    hideLoading();
                    onFilterChanged(null, null);
                }
            }
            else
            {
                throw new Exception(String.Format("File does not exist \"{0}\"", filePath));
            }
        }

    }
}
