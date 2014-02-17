using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Search_DBX_files
{
    class LoadedFiles
    {
        public Action OnComplete;
        private class ThreadState
        {
            public FileInfo File { get; set; }
            public StreamReader Content { get; set; }
            public bool IsDbxFile { get; set; }
            public string Filter { get; set; }
        }

        private ConcurrentBag<String> _strings;
        private System.IO.FileInfo[] _allDbxFiles;
        private System.IO.FileInfo[] _allCppFiles;
        private string _dbxFilter;
        private string _cppFilter;
        private DateTime _start;
        private ReadOnlyCollection<DiceItem> _identifiers;
        private Dictionary<String, List<FileInfo>> _resultingFiles;
        private Dictionary<String, List<String>> _resultingLines;
        private int _counter;
        private Object resultLock = new Object();

        public Dictionary<string, List<FileInfo>> FileResult { get { return _resultingFiles; } }
        public Dictionary<string, List<string>> LineResult { get { return _resultingLines; } }
        public int TotalFiles { get; private set; }
        public int FilesLeft { get { return _counter; } }
        private List<string> _regexDisabledMatches;

        public LoadedFiles(FileInfo[] dbxFiles, FileInfo[] cppFiles, string dbxFilter, string cppFilter, ReadOnlyCollection<DiceItem> readOnlyCollection, int maxThreads)
        {
            _regexDisabledMatches = new List<string>();
            _start = DateTime.Now;
            _strings = new ConcurrentBag<String>();
            _allDbxFiles = dbxFiles;
            _allCppFiles = cppFiles;
            _dbxFilter = dbxFilter;
            _cppFilter = cppFilter;
            _identifiers = readOnlyCollection;
            _resultingFiles = new Dictionary<String, List<FileInfo>>();
            _resultingLines = new Dictionary<String, List<String>>();
            _counter = _allDbxFiles.Length + _allCppFiles.Length;
            TotalFiles = _counter;

            if (TotalFiles > 0)
            {
                ThreadPool.SetMaxThreads(maxThreads * 2, maxThreads); //setting max threads lower than # cpu's doesn't have any affect due to MS being stupid and not allowing that functionality... ;/
                var bw = new BackgroundWorker();
                bw.DoWork += (sender, args) =>
                {
                    foreach (var file in _allDbxFiles)
                    {
                        if (file.Exists)
                        {
                            ThreadPool.QueueUserWorkItem(new WaitCallback(parseFile), new ThreadState { File = file, Content = new StreamReader(file.FullName), IsDbxFile = true, Filter = _dbxFilter });
                        }
                    }
                    foreach (var file in _allCppFiles)
                    {
                        using (var sr = new StreamReader(file.OpenRead()))
                        {
                            ThreadPool.QueueUserWorkItem(new WaitCallback(parseFile), new ThreadState { File = file, Content = new StreamReader(file.FullName), IsDbxFile = false, Filter = _cppFilter });
                        }
                    }
                    Console.WriteLine("foreach done in {0}s", (DateTime.Now - _start).TotalSeconds);
                };
                bw.RunWorkerCompleted += onThreadCompleted;
                bw.RunWorkerAsync();
            }
            else
            {
                allThreadsDone();
            }

        }

        private void parseFile(Object o)
        {
            Dictionary<string, List<FileInfo>> resultFiles = new Dictionary<string, List<FileInfo>>();
            Dictionary<string, List<string>> resultLines = new Dictionary<string, List<string>>();
            findUsage((ThreadState)o,ref resultFiles, ref resultLines);
            insertUsage(ref resultFiles, ref resultLines);
            int counter = Interlocked.Decrement(ref _counter);
            if (counter <= 0)
            {
                Console.WriteLine("Counter: {0}", counter);
                allThreadsDone();
            }
        }

        private void findUsage(ThreadState state, ref Dictionary<string, List<FileInfo>> resultFiles, ref Dictionary<string, List<string>> resultLines)
        {
            int lineNumber = 1;
            
            while(!state.Content.EndOfStream)            
            {
                string line = state.Content.ReadLine();
                if (line.Contains(state.Filter))
                {
                    //check the line against the supplied identifiers [guid/identifier]
                    for (int idCounter = 0; idCounter < _identifiers.Count; ++idCounter)
                    {
                        var item = _identifiers[idCounter];
                        var filteredId = state.IsDbxFile ? item.Guid : item.Identifier;
                        if (line.Contains(filteredId))
                        {
                            string valid = "[\\w_]+";
                            Regex match = new Regex(filteredId + valid);
                            if (!match.IsMatch(line))
                            {
                                if (!resultFiles.ContainsKey(item.Guid))
                                {
                                    resultFiles.Add(item.Guid, new List<FileInfo>());
                                }
                                resultFiles[item.Guid].Add(state.File);

                                if (!resultLines.ContainsKey(item.Guid))
                                {
                                    resultLines.Add(item.Guid, new List<string>());
                                }
                                resultLines[item.Guid].Add(String.Format("[{0}] {1}", lineNumber, line.Trim()));
                            }
                            else
                            {
                                _regexDisabledMatches.Add(String.Format(" filtered: {0}\n line: {1}\n file: {2} \n\n", filteredId, line, state.File.FullName));
                            }
                        }
                    }
                }
                lineNumber++;
            }
        }

        private void allThreadsDone()
        {
            Console.WriteLine("Everything completed in {0}s", (DateTime.Now - _start).TotalSeconds);
            if (OnComplete != null)
            {
                OnComplete.Invoke();
                using (var sw = new StreamWriter("./output/_regex_disabled_items.txt"))
                {
                    foreach (var line in _regexDisabledMatches)
                    {
                        sw.WriteLine(line);
                    }
                }
            }
        }

        /// <summary>
        /// Thread safe access of member variables
        /// </summary>
        private void insertUsage(ref Dictionary<string, List<FileInfo>> resultFiles, ref Dictionary<string, List<string>> resultLines)
        {
            //aquire lock
            lock (resultLock)
            {
                foreach (var i in resultFiles)
                {                    
                    if (!_resultingFiles.ContainsKey(i.Key))
                    {
                        _resultingFiles.Add(i.Key, i.Value);
                    }
                    else
                    {
                        _resultingFiles[i.Key].AddRange(i.Value);
                    }
                }
                foreach (var i in resultLines)
                {
                    if (!_resultingLines.ContainsKey(i.Key))
                    {
                        _resultingLines.Add(i.Key, i.Value);
                    }
                    else
                    {
                        _resultingLines[i.Key].AddRange(i.Value);
                    }
                }
            }
        }

        private void onThreadCompleted(object sender, RunWorkerCompletedEventArgs args)
        {
            if (args.Error != null)  // if an exception occurred during DoWork,
            {
                Console.WriteLine("ERROR: {0}", args.Error);
                MessageBox.Show(args.Error.ToString());  // do your error handling here
            }
            Console.WriteLine(String.Format("I/O Operations completed in {0} seconds", (DateTime.Now - _start).TotalSeconds));
        }
    }
}
