﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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

        public MainWindow()
        {
            InitializeComponent();
            hideLoading();
            loadSettings();
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
                            dbxFolder.Text = item;
                        }
                        else if (line.Contains("cpp_root"))
                        {
                            var foo = line.Split(splitter);
                            var item = foo[1].Trim();
                            cppRoot.Text = item;
                        }
                        else if (line.Contains("comp_file"))
                        {
                            var foo = line.Split(splitter);
                            var item = foo[1].Trim();
                            dataDefsFile.Text = item;
                        }
                        else if (line.Contains("dbx_filter"))
                        {
                            var foo = line.Split(splitter);
                            var item = foo[1].Trim();
                            dbxLineFilter.Text = item;
                        }
                        else if (line.Contains("cpp_filter"))
                        {
                            var foo = line.Split(splitter);
                            var item = foo[1].Trim();
                            cppLineFilter.Text = item;
                        }
                    }
                }
            }
            catch (Exception e)
            {

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

            var defFile = new FileInfo(dataDefsFile.Text);
            var dbxPath = new DirectoryInfo(dbxFolder.Text);
            var cppPath = new DirectoryInfo(cppRoot.Text);
            var cppFilter = cppLineFilter.Text;
            var dbxFilter = dbxLineFilter.Text;

            if (!validatePaths(defFile, dbxPath, cppPath))
            {
                MessageBox.Show("Invalid path to datadefines, dbx folder or cpp folder");
            }
            else
            {
                showLoading();
                new System.Threading.Timer(obj => {
                    populateIdentifiers(defFile);

                    this.Dispatcher.Invoke((Action)(() =>
                    {
                        identifiersList.ItemsSource = _items;
                    }));
                    _allDbxFiles = dbxPath.GetFiles("*.dbx", SearchOption.AllDirectories);
                    _allCppFiles = cppPath.GetFiles("*.cpp", SearchOption.AllDirectories);

                    analyzeFiles(defFile, dbxPath, cppPath, dbxFilter, cppFilter); 
                }, null, 50, System.Threading.Timeout.Infinite);
            }
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
            var threadSafeCollection = _items.AsReadOnly();

            for (var i = 0; i < cpusDbx; ++i)
            {
                int delta = (int)Math.Ceiling(_allDbxFiles.Length / (double)cpusDbx);
                int tStart = delta * i;
                int tEnd = Math.Min(delta * (i + 1), _allDbxFiles.Length);

                var bw = new BackgroundWorker();
                bw.DoWork += (sender, args) => {
                    this.findUsage(_allDbxFiles, threadSafeCollection, dbxFilter, tStart, tEnd, true);
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
                    this.findUsage(_allCppFiles, threadSafeCollection, cppFilter, tStart, tEnd, false);
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

        private void increaseFilesCompleted(int value)
        {
            Interlocked.Add(ref _filesCompleted, value);

            Console.WriteLine("Files gone through: " + _filesCompleted);
            fileStatus.Content = "FileStatus: " + _filesCompleted + " / " + (_allCppFiles.Length + _allDbxFiles.Length);
            guidStatus.Content = "Identifiers: " + _items.Count;
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
            hideLoading();
            updateIdentifiers();
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
            var fileId = DateTime.Now.Ticks;
            var di = new DirectoryInfo("./");
            di.CreateSubdirectory("./output/");
            var f = "./output/" + fileId;

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
            //_timestamp = DateTime.Now.Ticks;
            var fileCounter = 0;
            //foreach (var file in files)
            for (int i = startIndex; i < endIndex; ++i)
            {
                var file = files[i];
                if (fileCounter > 0 && fileCounter % 50 == 0)
                {
                    this.Dispatcher.Invoke((Action)(() =>
                    {
                        increaseFilesCompleted(fileCounter);
                        fileCounter = 0;
                    }));
                }
                //if (id++ > 100) break;
                String[] lines;
                using (var sr = new StreamReader(file.OpenRead()))
                {
                    lines = sr.ReadToEnd().Split(Environment.NewLine.ToCharArray());
                }
                int lineNumber = 0;
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
            btn.IsEnabled = false;
            blocker1.Visibility = Visibility.Visible;
            blocker2.Visibility = Visibility.Visible;
            fileStatus.Visibility = Visibility.Visible;
            guidStatus.Visibility = Visibility.Visible;
            identifiersList.IsEnabled = false;
        }

        private void hideLoading()
        {
            //btn.IsEnabled = true;
            blocker1.Visibility = Visibility.Hidden;
            blocker2.Visibility = Visibility.Hidden;
            fileStatus.Visibility = Visibility.Hidden;
            guidStatus.Visibility = Visibility.Hidden;
            identifiersList.IsEnabled = true;
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
        }

    }
}