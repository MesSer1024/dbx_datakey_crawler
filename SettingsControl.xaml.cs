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

        public SettingsControl()
        {
            InitializeComponent();
        }

        public void loadSettings(string path)
        {
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
    }
}
