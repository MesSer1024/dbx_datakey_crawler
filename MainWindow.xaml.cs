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


    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public partial class MainWindow : Window
    {
        private Dictionary<String, List<FileInfo>> _resultingFiles;
        private Dictionary<String, List<String>> _resultingLines;
        private List<DiceItem> _items;
        private List<DiceItem> _suspects;

        private FileInfo[] _allDbxFiles;
        private FileInfo[] _allCppFiles;
        private long _timestamp;
        private long _totalTime;
        private int _unfinishedThreads;
        private int _filesCompleted;
        private long _fileId;

        public MainWindow()
        {
            InitializeComponent();
            hideLoading();
            loadSettings();

            settingsControl.onAnalyzeButton += settingsControl_onAnalyzeButton;
            settingsControl.onLoadButton += settingsControl_onLoadButton;
            resultGrid.Visibility = Visibility.Collapsed;
        }

        void settingsControl_onLoadButton()
        {
            onLoad(null, null);
        }

        void settingsControl_onAnalyzeButton()
        {
            onAnalyze(null, null);
        }            

        private void loadSettings()
        {
            try
            {
                using (var sr = new StreamReader("./config.ini"))
                {
                    while (!sr.EndOfStream)
                    {
                        var splitter = '|';
                        var line = sr.ReadLine();
                        if (line.Contains("dbx_root"))
                        {
                            var foo = line.Split(splitter);
                            var item = foo[1].Trim();
                            settingsControl.DbxRoot = item;
                        }
                        else if (line.Contains("cpp_root"))
                        {
                            var foo = line.Split(splitter);
                            var item = foo[1].Trim();
                            settingsControl.CppRoot = item;
                        }
                        else if (line.Contains("comp_file"))
                        {
                            var foo = line.Split(splitter);
                            var item = foo[1].Trim();
                            settingsControl.DataDefinesFile = item;
                        }
                        else if (line.Contains("dbx_filter"))
                        {
                            var foo = line.Split(splitter);
                            var item = foo[1].Trim();
                            settingsControl.DbxLineFilter = item;
                        }
                        else if (line.Contains("cpp_filter"))
                        {
                            var foo = line.Split(splitter);
                            var item = foo[1].Trim();
                            settingsControl.CppLineFilter = item;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception("Corrupt config-file! Please reinstall the product or get a valid one in another way! \n\n" + e.ToString());
            }
        }

        private void onAnalyze(object sender, RoutedEventArgs e)
        {
            _items = new List<DiceItem>();
            _items.Clear();
            identifiersList.ItemsSource = _items;
            _unfinishedThreads = 0;
            _filesCompleted = 0;
            _allDbxFiles = new FileInfo[] { };
            _allCppFiles = new FileInfo[] { };
            _resultingFiles = new Dictionary<String, List<FileInfo>>();
            _resultingLines = new Dictionary<String, List<String>>();

            var defFile = new FileInfo(settingsControl.DataDefinesFile);
            var dbxPath = new DirectoryInfo(settingsControl.DbxRoot);
            var cppPath = new DirectoryInfo(settingsControl.CppRoot);
            var cppFilter = settingsControl.CppLineFilter;
            var dbxFilter = settingsControl.DbxLineFilter;

            if (!validatePaths(defFile, dbxPath, cppPath))
            {
                MessageBox.Show("Invalid path to datadefines, dbx folder or cpp folder");
            }
            else
            {
                showLoading();
                updateThePopulationText("Populating files [You should see progress within ~10s]");
                new System.Threading.Timer(obj => {
                    populateIdentifiers(defFile);
                    this.Dispatcher.Invoke((Action)(() =>
                    {
                        identifiersList.ItemsSource = _items;
                        identifiersList.Items.Refresh();
                    }));
                    _allDbxFiles = dbxPath.GetFiles("*.dbx", SearchOption.AllDirectories);
                    _allCppFiles = cppPath.GetFiles("*.cpp", SearchOption.AllDirectories);
                    updateThePopulationText("DBX/CPP Files populated, searching in files");
                    analyzeFiles(defFile, dbxPath, cppPath, dbxFilter, cppFilter); 
                }, null, 50, System.Threading.Timeout.Infinite);
            }
        }

        private void updateThePopulationText(string s)
        {
            this.Dispatcher.Invoke((Action)(() =>
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
                                MessageBox.Show("Could not find valid ID and Guid from: " + line);
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

        private void analyzeFiles(FileInfo defFile, DirectoryInfo dbxPath, DirectoryInfo cppPath, string dbxFilter, string cppFilter)
        {
            _unfinishedThreads = Environment.ProcessorCount;
            int cpusDbx = _unfinishedThreads / 2;
            int cpusCpp = _unfinishedThreads - cpusDbx;

            _timestamp = DateTime.Now.Ticks;
            var readOnlyCollection = _items.AsReadOnly();

            for (var i = 0; i < cpusDbx; ++i)
            {
                int delta = (int)Math.Ceiling(_allDbxFiles.Length / (double)cpusDbx);
                int tStart = delta * i;
                int tEnd = Math.Min(delta * (i + 1), _allDbxFiles.Length);

                var bw = new BackgroundWorker();
                bw.DoWork += (sender, args) => {
                    this.findUsage(_allDbxFiles, readOnlyCollection, dbxFilter, tStart, tEnd, true);
                };

                bw.RunWorkerCompleted += onThreadError;
                bw.RunWorkerAsync();
            }

            for (var i = 0; i < cpusCpp; ++i)
            {
                int delta = (int)Math.Ceiling(_allCppFiles.Length / (double)cpusCpp);
                int tStart = delta * i;
                int tEnd = Math.Min(delta * (i + 1), _allCppFiles.Length);

                var bw = new BackgroundWorker();
                bw.DoWork += (sender, args) =>
                {
                    this.findUsage(_allCppFiles, readOnlyCollection, cppFilter, tStart, tEnd, false);
                };

                bw.RunWorkerCompleted += onThreadError;
                bw.RunWorkerAsync();
            }
        }

        private void onThreadError(object sender, RunWorkerCompletedEventArgs args)
        {
            if (args.Error != null)  // if an exception occurred during DoWork,
                MessageBox.Show(args.Error.ToString());  // do your error handling here
            threadDone();
        }

        /// <summary>
        /// Increase the amount of files that are completed and update the gui
        /// </summary>
        /// <param name="value"></param>
        private void increaseFilesCompleted(int value)
        {
            Interlocked.Add(ref _filesCompleted, value);

            Console.WriteLine("Files gone through: " + _filesCompleted);
            var totalFiles = _allCppFiles.Length + _allDbxFiles.Length;
            
            fileStatus.Content = String.Format("Files completed: {0}/{1}", _filesCompleted, _allCppFiles.Length + _allDbxFiles.Length);
            //guidStatus.Content = String.Format("Amount of keys: {0}", _items.Count);
            guidStatus.Content = "";
            var approximateLineCount = 1477;
            long linesToGoThrough = (long)(totalFiles - _filesCompleted) * (long)_items.Count * (long)approximateLineCount;
            updateThePopulationText(String.Format("Hang on, I just have another {0} potential lines to go through or so ...", linesToGoThrough));
        }

        private void threadDone()
        {
            if (Interlocked.Decrement(ref _unfinishedThreads) <= 0)
            {
                _totalTime = DateTime.Now.Ticks - _timestamp;
                Console.WriteLine("Total Execution Time: {0}, startTime: {1}", _totalTime, _timestamp);

                this.Dispatcher.Invoke((Action)(() =>
                {
                    handleAllDataAvailable();
                }));
            }
        }

        private void handleAllDataAvailable()
        {
            populateSuspects();
            outputToFile();
            updateIdentifiers();
            saveAsJson();
            hideLoading();
            onFilterChanged(null, null);
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
            //#TODO: find all items that does not have any references
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

        private void findUsage(FileInfo[] files, ReadOnlyCollection<DiceItem> identifiers, String filter, int startIndex, int endIndex, bool checkDbxFiles)
        {
            var fileCounter = 0;
            //navigate through a subset of all the files (files from start_index to end_index)
            for (int i = startIndex; i < endIndex; ++i)
            {
                var file = files[i];
                //report progress to gui every 50 files or so
                if (fileCounter % 50 == 0 && fileCounter > 0)
                {
                    this.Dispatcher.Invoke((Action)(() =>
                    {
                        increaseFilesCompleted(fileCounter);
                        fileCounter = 0;
                    }));
                }
                String[] lines;
                using (var sr = new StreamReader(file.OpenRead()))
                {
                    var input = sr.ReadToEnd();
                    lines = Regex.Split(input, "\r\n|\r|\n");
                }
                int lineNumber = 1;
                foreach (var line in lines)
                {
                    if (line.Contains(filter))
                    {
                        //check the line against the supplied identifiers [guid/identifier]
                        for (int idCounter = 0; idCounter < identifiers.Count; ++idCounter)
                        {
                            var item = identifiers[idCounter];
                            var filteredId = checkDbxFiles ? item.Guid : item.Identifier;
                            if (line.Contains(filteredId))
                            {
                                insertUsage(item.Guid, file);
                                insertLine(item.Guid, line, lineNumber);
                            }
                        }
                    }
                    lineNumber++;
                }
                fileCounter++;
            }
            //_totalTime = DateTime.Now.Ticks - _timestamp;
            //Console.WriteLine("Total Execution Time: {0}, startTime: {1}", _totalTime, _timestamp);
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
            //settingsControl.MaxHeight = 50;
            //settingsControl.Visibility = Visibility.Collapsed;
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
            //Issue since everything is based on indexes, and those are messed up after filtering
            
            if (_items == null)
            {
                return;
            }
            var filter = idFilter.Text;
            if (filter != "")
            {
                identifiersList.ItemsSource = _items.FindAll(a => a.VisibleName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
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
            //filePath = "./output/635092303488279385.dcs";
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
