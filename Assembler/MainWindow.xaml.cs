using System;
using System.Collections.Generic;
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
using System.Windows.Forms;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace Assembler
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    ///
    public enum Register
    {
        UNKNOWN = 0,
        AL = 1,
        AH = 2,
        A = 4,
        B = 8,
        C = 16,
    }

    enum ParseState
    {
        SUCCESSFUL = 0,
        UNKNOWN_REGISTER,
        INCORRECT_VALUE,
        ILLEGAL_INSTRUCTION
    }

    public partial class MainWindow : Window
    {
        private Dictionary<string, UInt16> labelDict;
        private UInt16 binaryFileLength;
        private UInt16 entryAddress;
        private UInt16 offset;


        public MainWindow()
        {
            InitializeComponent();
            labelDict = new Dictionary<string, ushort>();
            binaryFileLength = 0;
            entryAddress = 0;
        }

        private void openButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "VM Source Assembly File(*.vmasm)|*.vmasm";
            openFileDialog.DefaultExt = "vmasm";
            openFileDialog.FileName = string.Empty;
            openFileDialog.InitialDirectory = Directory.GetCurrentDirectory();

            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                openTextBox.Text = openFileDialog.FileName;
            else
                openTextBox.Clear();
        }

        private void assemblyButton_Click(object sender, RoutedEventArgs e)
        {
            if (openTextBox.Text == string.Empty)
            {
                System.Windows.MessageBox.Show("No input file.", "Fatal error!", MessageBoxButton.OK);
                return;
            }

            var sourceFilePath = openTextBox.Text;
            var outputFilePath = browseTextBox.Text;
            if (outputFilePath == string.Empty)
                outputFilePath = Directory.GetCurrentDirectory();
            offset = offsetTextBox.Text == string.Empty ? (UInt16)512 : Convert.ToUInt16(offsetTextBox.Text, 16);

            labelDict.Clear();
            binaryFileLength = offset;

            var fileInfo = new FileInfo(sourceFilePath);
            var fileStream = new FileStream(System.IO.Path.Combine(outputFilePath, fileInfo.Name + ".vmbin"), FileMode.Create);
            var output = new BinaryWriter(fileStream);

            progressBar.Value = progressBar.Minimum;

            //Magic word
            output.Write('Z');
            output.Write('H');
            output.Write('A');
            output.Write('N');
            output.Write('G');
            output.Write('S');
            output.Write('H');
            output.Write('U');

            output.Write(offset);
            output.Seek((int)offset, SeekOrigin.Begin);

            TextReader sourceFile = File.OpenText(sourceFilePath);
            var totalLinesCount = File.ReadAllLines(sourceFilePath).Count();

            string line = string.Empty;
            int rowNumber = 1;
            while ((line = sourceFile.ReadLine()) != null)
            {
                var parseState = parse(line.ToUpper(), output);
                if (parseState != ParseState.SUCCESSFUL)
                {
                    string errorMessage = string.Empty;
                    switch (parseState)
                    {
                        case ParseState.ILLEGAL_INSTRUCTION:
                            errorMessage = "UNSUPPORTED INSTRUCTION IN LINE: " + rowNumber.ToString();
                            break;
                        case ParseState.INCORRECT_VALUE:
                            errorMessage = "ILLEGAL NUMBER IN LINE: " + rowNumber.ToString();
                            break;
                        case ParseState.UNKNOWN_REGISTER:
                            errorMessage = "THE REGISTER IS NOT EXIST IN LINE: " + rowNumber.ToString();
                            break;
                        default:
                            break;
                    }
                    System.Windows.MessageBox.Show(errorMessage, "Error!", MessageBoxButton.OK);
                    return;
                }
                progressBar.Value = rowNumber / totalLinesCount;
            }
            sourceFile.Close();

            output.Seek(10, SeekOrigin.Begin);
            output.Write(binaryFileLength);
            output.Write(entryAddress);
            output.Close();
            fileStream.Close();

            System.Windows.MessageBox.Show("", "Done!", MessageBoxButton.OK);
        }

        private ParseState parse(string line, BinaryWriter output)
        {
            line = cleanLine(line);

            if (line == string.Empty)
                return ParseState.SUCCESSFUL;

            if (line.EndsWith(":"))
                labelDict.Add(line.TrimEnd(new char[] { ':' }), binaryFileLength);
            else
            {
                var match = Regex.Match(line, @"(\w+)\s+(.+)\s+(.+)");
                string opcode = match.Groups[1].Value;
                string operandLeft = match.Groups[2].Value;
                string operandRight = match.Groups[3].Value;

                switch (opcode)
                {
                    case "LDT":
                        {
                            output.Write((byte)0x01);

                            var register = getRegister(operandLeft);
                            if (register == Register.UNKNOWN)
                                return ParseState.UNKNOWN_REGISTER; 
                            output.Write((byte)register);

                            var valueRight = getWordValue(operandRight);
                            if (valueRight.HasValue == false)
                                return ParseState.INCORRECT_VALUE;
                            output.Write((UInt16)valueRight);
                            binaryFileLength += 4;
                            break;
                        }
                    case "STT":
                        {
                            output.Write((byte)0x02);

                            var valueLeft = getWordValue(operandLeft);
                            if (valueLeft.HasValue == false)
                                return ParseState.INCORRECT_VALUE;
                            output.Write((UInt16)valueLeft);

                            var register = getRegister(operandRight);
                            if (register == Register.UNKNOWN)
                                return ParseState.UNKNOWN_REGISTER;
                            output.Write((byte)register);
                            binaryFileLength += 4;
                            break;
                        }
                    case "END":
                        output.Write((byte)0x03);

                        if (operandLeft != "FOR")
                            return ParseState.ILLEGAL_INSTRUCTION;

                        if (labelDict.ContainsKey(operandRight))
                        {
                            output.Write(labelDict[operandRight]);
                            binaryFileLength += 2;
                        }
                        binaryFileLength += 1;
                        break;
                    default:
                        return ParseState.ILLEGAL_INSTRUCTION;
                }
            }
            return ParseState.SUCCESSFUL;
        }

        private string cleanLine(string line)
        {
            var ret = line.Trim();
            try
            {
                ret = ret.Substring(0, ret.IndexOf('!'));
            }
            catch
            {
            }
            return ret;
        }

        private Register getRegister(string operand)
        {
            switch (operand)
            {
                case "AL":
                    return Register.AL;
                case "AH":
                    return Register.AH;
                case "A":
                    return Register.A;
                case "B":
                    return Register.B;
                case "C":
                    return Register.C;
                default:
                    return Register.UNKNOWN;
            }
        }

        private byte? getByteValue(string operand)
        {
            byte? ret = null;
            if (operand.StartsWith("#"))
            {
                operand = operand.Remove(0, 1);
                char last = operand[operand.Length - 1];
                if (char.IsLetter(last))
                {
                    operand = operand.Remove(operand.Length - 1, 1);
                    switch (last)
                    {
                        case 'H':
                            ret = Convert.ToByte(operand, 16);
                            return ret;
                        case 'O':
                            ret = Convert.ToByte(operand, 8);
                            return ret;
                        case 'D':
                            ret = Convert.ToByte(operand, 10);
                            return ret;
                        default:
                            return 0;
                    }
                }
                else
                {
                    ret = Convert.ToByte(operand, 10);
                    return ret;
                }
            }
            else
                return ret;
        }

        private UInt16? getWordValue(string operand)
        {
            UInt16? ret = null;
            if (operand.StartsWith("#"))
            {
                operand = operand.Remove(0, 1);
                char last = operand[operand.Length - 1];
                if (char.IsLetter(last))
                {
                    operand = operand.Remove(operand.Length - 1, 1);
                    switch (last)
                    {
                        case 'H':
                            ret = Convert.ToUInt16(operand, 16);
                            return ret;
                        case 'O':
                            ret = Convert.ToUInt16(operand, 8);
                            return ret;
                        case 'D':
                            ret = Convert.ToUInt16(operand, 10);
                            return ret;
                        default:
                            return ret;
                    }
                }
                else
                {
                    ret = Convert.ToUInt16(operand, 10);
                    return ret;
                }
            }
            else
                return ret;
        }

        private void browseButton_Click(object sender, RoutedEventArgs e)
        {
            var folderBrowseDialog = new FolderBrowserDialog();
            if (folderBrowseDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                browseTextBox.Text = folderBrowseDialog.SelectedPath;
            else
                browseTextBox.Clear();
        }
    }
}
