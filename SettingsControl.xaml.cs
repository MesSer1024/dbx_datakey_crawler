using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    /// Interaction logic for SettingsControl.xaml
    /// </summary>
    public partial class SettingsControl : UserControl
    {
        public delegate void VoidDelegate();

        public event VoidDelegate onAnalyzeButton;
        public event VoidDelegate onLoadButton;

        public String DbxRoot
        {
            get { return dbxFolder.Text; }
            set { dbxFolder.Text = value; }
        }

        public String CppRoot
        {
            get { return cppRoot.Text; }
            set { cppRoot.Text = value; }
        }

        public String DataDefinesFile
        {
            get { return dataDefsFile.Text; }
            set { dataDefsFile.Text = value; }
        }

        public String DbxLineFilter
        {
            get { return dbxLineFilter.Text; }
            set { dbxLineFilter.Text = value; }
        }

        public String CppLineFilter
        {
            get { return cppLineFilter.Text; }
            set { cppLineFilter.Text = value; }
        }

        public int MaxThreads
        {
            get { return int.Parse(maxThreads.Text); }
            set { maxThreads.Text = value.ToString(); }
        }

        public SettingsControl()
        {
            InitializeComponent();
        }

        public void loadSettings(string path)
        {
            MaxThreads = System.Environment.ProcessorCount;
            using (var sr = new StreamReader(path))
            {
                while (!sr.EndOfStream)
                {
                    var splitter = '|';
                    var line = sr.ReadLine();
                    if (line.Contains("dbx_root"))
                    {
                        var foo = line.Split(splitter);
                        var item = foo[1].Trim();
                        DbxRoot = item;
                    }
                    else if (line.Contains("cpp_root"))
                    {
                        var foo = line.Split(splitter);
                        var item = foo[1].Trim();
                        CppRoot = item;
                    }
                    else if (line.Contains("comp_file"))
                    {
                        var foo = line.Split(splitter);
                        var item = foo[1].Trim();
                        DataDefinesFile = item;
                    }
                    else if (line.Contains("dbx_filter"))
                    {
                        var foo = line.Split(splitter);
                        var item = foo[1].Trim();
                        DbxLineFilter = item;
                    }
                    else if (line.Contains("cpp_filter"))
                    {
                        var foo = line.Split(splitter);
                        var item = foo[1].Trim();
                        CppLineFilter = item;
                    }
                }
            }
        }

        private void onAnalyze(object sender, RoutedEventArgs e)
        {
            if (onAnalyzeButton != null)
            {
                onAnalyzeButton.Invoke();
            }
        }

        private void onLoad(object sender, RoutedEventArgs e)
        {
            if (onLoadButton != null)
            {
                onLoadButton.Invoke();
            }
        }

        public bool EnableAnalyzeButton
        {
            get { return btn.IsEnabled; }
            set { btn.IsEnabled = value; }
        }

        public bool EnableLoadButton
        {
            get { return loadBtn.IsEnabled; }
            set { loadBtn.IsEnabled = value; }
        }

        private void onThreadKeydown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.D0:
                case Key.D1:
                case Key.D2:
                case Key.D3:
                case Key.D4:
                case Key.D5:
                case Key.D6:
                case Key.D7:
                case Key.D8:
                case Key.D9:
                case Key.NumLock:
                case Key.NumPad0:
                case Key.NumPad1:
                case Key.NumPad2:
                case Key.NumPad3:
                case Key.NumPad4:
                case Key.NumPad5:
                case Key.NumPad6:
                case Key.NumPad7:
                case Key.NumPad8:
                case Key.NumPad9:
                case Key.Back:
                case Key.Enter:
                    break;
                default:
                    e.Handled = true;
                    break;
            }
        }

        internal void setEnabled(bool flag)
        {
            cppLineFilter.IsReadOnly = flag;
            dbxLineFilter.IsReadOnly = flag;
            cppRoot.IsReadOnly = flag;
            dbxFolder.IsReadOnly = flag;
            dataDefsFile.IsReadOnly = flag;
            btn.IsEnabled = flag;
            loadBtn.IsEnabled = flag;
        }
    }
}
