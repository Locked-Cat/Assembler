﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.IO;
using System.Text.RegularExpressions;

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
        D = 32
    }

    enum ParseState
    {
        SUCCESSFUL = 0,
        UNKNOWN_REGISTER,
        INCORRECT_VALUE,
        ILLEGAL_INSTRUCTION,
        INCORRECT_LABEL,
        UNMATCHED_OPERANDS,
        PROGRAM_END
    }

    public partial class MainWindow : Window
    {
        private Dictionary<string, UInt16> labelDict;
        private Dictionary<UInt16, Tuple<UInt16, string>> fillDict;
        private UInt16 binaryFileLength;
        private UInt16 startAddress;
        private UInt16 offset;

        public MainWindow()
        {
            InitializeComponent();
            labelDict = new Dictionary<string, UInt16>();
            fillDict = new Dictionary<UInt16, Tuple<UInt16, string>>();
            binaryFileLength = 0;
            startAddress = 0;
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

            progressBar.Value = 0;
        }

        private void assemblyButton_Click(object sender, RoutedEventArgs e)
        {
            if (openTextBox.Text == string.Empty)
            {
                System.Windows.MessageBox.Show("No input file.", "Fatal error!", MessageBoxButton.OK);
                return;
            }

            labelDict.Clear();
            fillDict.Clear();

            var sourceFilePath = openTextBox.Text;
            var outputFilePath = browseTextBox.Text;
            if (outputFilePath == string.Empty)
                outputFilePath = Directory.GetCurrentDirectory();
            offset = offsetTextBox.Text == string.Empty ? (UInt16)512 : Convert.ToUInt16(offsetTextBox.Text, 16);

            binaryFileLength = offset;
            labelProgress.Content = "Progress: 0%";

            var fileInfo = new FileInfo(sourceFilePath);
            var fileStream = new FileStream(System.IO.Path.Combine(outputFilePath, fileInfo.Name.Substring(0, fileInfo.Name.LastIndexOf('.')) + ".vmbin"), FileMode.Create);
            var output = new BinaryWriter(fileStream);

            progressBar.Value = progressBar.Minimum;

            //magic word
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
            
            var line = string.Empty;
            var rowNumber = (UInt16)1;
            while ((line = sourceFile.ReadLine()) != null)
            {
                var parseState = parse(line.ToUpper(), output, rowNumber);
                if (parseState != ParseState.SUCCESSFUL && parseState != ParseState.PROGRAM_END)
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
                        case ParseState.INCORRECT_LABEL:
                            errorMessage = "THE LABEL IS NOT EXIST IN LINE: " + rowNumber.ToString();
                            break;
                        case ParseState.UNMATCHED_OPERANDS:
                            errorMessage = "THE OPERANDS ARE NOT SAME IN LINE: " + rowNumber.ToString();
                            break;
                        default:
                            break;
                    }
                    System.Windows.MessageBox.Show(errorMessage, "Error!", MessageBoxButton.OK);
                    sourceFile.Close();
                    output.Close();
                    fileStream.Close();
                    return;
                }
                if (parseState == ParseState.SUCCESSFUL)
                {
                    progressBar.Value = progressBar.Maximum * rowNumber / totalLinesCount / 2;
                    labelProgress.Content = "Progress: " + progressBar.Value.ToString("0.0") + "%";
                    rowNumber += 1;
                }
                else
                {
                    //ParseState.PROGRAM_END
                    progressBar.Value = progressBar.Maximum / 2;
                    labelProgress.Content = "Progress: " + progressBar.Value.ToString("0.0") + "%";
                    break;
                }
            }
            sourceFile.Close();

            var totalItemsCount = fillDict.Count;
            var i = 0;

            foreach (var item in fillDict)
            {
                output.Seek(item.Key, SeekOrigin.Begin);

                if (labelDict.ContainsKey(item.Value.Item2))
                {
                    output.Write((Int16)(labelDict[item.Value.Item2] - item.Key));
                    i += 1;
                    progressBar.Value = progressBar.Maximum / 2 +  progressBar.Maximum / 2 * i / totalItemsCount;
                    labelProgress.Content = "Progress: " + progressBar.Value.ToString("0.0") + "%";
                }
                else
                {
                    var errorMessage = "THE LABEL IS NOT EXIST IN LINE: " + item.Value.Item1.ToString();
                    System.Windows.MessageBox.Show(errorMessage, "Error!", MessageBoxButton.OK);
                    output.Close();
                    fileStream.Close();
                    return;
                }
            }

            output.Seek(10, SeekOrigin.Begin);
            output.Write(binaryFileLength);
            output.Write(startAddress);
            output.Close();
            fileStream.Close();

            System.Windows.MessageBox.Show("Done!", "Successful!", MessageBoxButton.OK);
        }

        private ParseState parse(string line, BinaryWriter output, UInt16 rowNumber)
        {
            cleanLine(ref line);

            if (line == string.Empty)
                return ParseState.SUCCESSFUL;

            if (line.EndsWith(":"))
            {
                labelDict.Add(line.TrimEnd(new char[] { ':' }), binaryFileLength);
                return ParseState.SUCCESSFUL;
            }
            else
            {
                if (line.StartsWith("END") ||
                    line.StartsWith("JMP") ||
                    line.StartsWith("JLE") || line.StartsWith("JL") ||
                    line.StartsWith("JGE") || line.StartsWith("JG") ||
                    line.StartsWith("JNE") || line.StartsWith("JE"))
                {
                    var match = Regex.Match(line, @"(\w+)\s+(.+)");
                    var opcode = match.Groups[1].Value;
                    var label = match.Groups[2].Value;

                    switch (opcode)
                    {
                        case "END":
                            {
                                output.Write((byte)0x04);
                                if (labelDict.ContainsKey(label))
                                {
                                    output.Write(labelDict[label]);
                                    binaryFileLength += 3;
                                    return ParseState.PROGRAM_END;
                                }
                                else
                                    return ParseState.INCORRECT_LABEL;
                            }
                        case "JMP":
                            output.Write((byte)0x05);
                            break;
                        case "JLE":
                            output.Write((byte)0x06);
                            break;
                        case "JL":
                            output.Write((byte)0x07);
                            break;
                        case "JGE":
                            output.Write((byte)0x08);
                            break;
                        case "JG":
                            output.Write((byte)0x09);
                            break;
                        case "JE":
                            output.Write((byte)0x0a);
                            break;
                        case "JNE":
                            output.Write((byte)0x0b);
                            break;
                    }
                        
                    binaryFileLength += 1;
                    fillDict.Add(binaryFileLength, new Tuple<ushort, string>(rowNumber, label));
                    output.Write((UInt16)0x0000);
                    binaryFileLength += 2;
                    return ParseState.SUCCESSFUL;
                }
                else
                {
                    var match = Regex.Match(line, @"(\w+)\s+(.+)\s+(.+)");
                    string opcode = match.Groups[1].Value;
                    string leftOperand = match.Groups[2].Value;
                    string rightOperand = match.Groups[3].Value;

                    switch (opcode)
                    {
                        case "LDT":
                            {
                                var leftRegister = getRegister(leftOperand);
                                if (leftRegister == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                if (rightOperand.StartsWith("#") || rightOperand.StartsWith("$"))
                                {
                                    output.Write((byte)0x01);

                                    output.Write((byte)leftRegister);
                                    var rightValue = getWordValue(rightOperand);
                                    if (rightValue.HasValue == false)
                                        return ParseState.INCORRECT_VALUE;
                                    output.Write((UInt16)rightValue);
                                    binaryFileLength += 4;
                                }
                                else
                                {
                                    output.Write((byte)0x28);
                                    var rightRegister = getRegister(rightOperand);
                                    if (rightRegister == Register.UNKNOWN)
                                        return ParseState.UNKNOWN_REGISTER;

                                    output.Write((byte)leftRegister);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 3;
                                }
                                break;
                            }
                        case "STT":
                            {
                                var rightRegister = getRegister(rightOperand);
                                if (rightRegister == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                if (leftOperand.StartsWith("#") || leftOperand.StartsWith("$"))
                                {
                                    output.Write((byte)0x02);

                                    var leftValue = getWordValue(leftOperand);
                                    if (leftValue.HasValue == false)
                                        return ParseState.INCORRECT_VALUE;

                                    output.Write((UInt16)leftValue);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 4;
                                }
                                else
                                {
                                    output.Write((byte)0x27);

                                    var leftRegister = getRegister(leftOperand);
                                    if (leftRegister == Register.UNKNOWN)
                                        return ParseState.UNKNOWN_REGISTER;

                                    output.Write((byte)leftRegister);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 3;
                                }
                                break;
                            }
                        case "SET":
                            {
                                output.Write((byte)0x03);

                                var register = getRegister(leftOperand);
                                if (register == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;
                                output.Write((byte)register);

                                if (register == Register.AH || register == Register.AL)
                                {
                                    var rightValue = getByteValue(rightOperand);
                                    if (!rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;
                                    output.Write(rightValue.Value);
                                    output.Write((byte)0x00);
                                }
                                else
                                {
                                    var rightValue = getWordValue(rightOperand);
                                    if (!rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;
                                    output.Write(rightValue.Value);
                                }
                                binaryFileLength += 4;
                                break;
                            }
                        case "CMP":
                            {
                                if ((leftOperand.StartsWith("#") || leftOperand.StartsWith("$")) && (rightOperand.StartsWith("#") || rightOperand.StartsWith("$")))         //CMP V V
                                {
                                    output.Write((byte)0x0c);

                                    var leftValue = getWordValue(leftOperand);
                                    var rightValue = getWordValue(rightOperand);
                                    if (!leftValue.HasValue || !rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;

                                    output.Write(leftValue.Value);
                                    output.Write(rightValue.Value);
                                    binaryFileLength += 5;
                                }
                                else
                                {
                                    if (leftOperand.StartsWith("#") || leftOperand.StartsWith("$"))                                        //CMP V R
                                    {
                                        output.Write((byte)0x0d);

                                        var leftValue = getWordValue(leftOperand);
                                        if (!leftValue.HasValue)
                                            return ParseState.INCORRECT_VALUE;

                                        var register = getRegister(rightOperand);
                                        if (register == Register.UNKNOWN)
                                            return ParseState.UNKNOWN_REGISTER;

                                        if ((register == Register.AH || register == Register.AL) && leftValue.Value > 0xff)
                                            return ParseState.UNMATCHED_OPERANDS;

                                        output.Write(leftValue.Value);
                                        output.Write((byte)register);
                                        binaryFileLength += 4;
                                    }
                                    else
                                    {
                                        if (rightOperand.StartsWith("#") || rightOperand.StartsWith("$"))                                   //CMP R V
                                        {
                                            output.Write((byte)0x0e);

                                            var register = getRegister(leftOperand);
                                            if (register == Register.UNKNOWN)
                                                return ParseState.UNKNOWN_REGISTER;

                                            var rightValue = getWordValue(rightOperand);
                                            if (!rightValue.HasValue)
                                                return ParseState.INCORRECT_VALUE;

                                            if ((register == Register.AH || register == Register.AL) && rightValue.Value > 0xff)
                                                return ParseState.UNMATCHED_OPERANDS;

                                            output.Write((byte)register);
                                            output.Write(rightValue.Value);
                                            binaryFileLength += 4;
                                        }
                                        else                                                                //CMP R R
                                        {
                                            output.Write((byte)0x0f);

                                            var leftRegister = getRegister(leftOperand);
                                            var rightRegister = getRegister(rightOperand);

                                            if (leftRegister == Register.UNKNOWN || rightRegister == Register.UNKNOWN)
                                                return ParseState.UNKNOWN_REGISTER;

                                            if (((leftRegister == Register.AH || leftRegister == Register.AL) && (rightRegister != Register.AH && rightRegister != Register.AL)) ||
                                                ((rightRegister == Register.AH || rightRegister == Register.AL) && (leftRegister != Register.AH && leftRegister != Register.AL)))
                                                return ParseState.UNMATCHED_OPERANDS;

                                            output.Write((byte)leftRegister);
                                            output.Write((byte)rightRegister);
                                            binaryFileLength += 3;
                                        }
                                    }
                                }
                                break;
                            }
                        case "CPY":     //CPY R R
                            {
                                output.Write((byte)0x10);

                                var leftRegister = getRegister(leftOperand);
                                var rightRegister = getRegister(rightOperand);

                                if (leftRegister == Register.UNKNOWN || rightRegister == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                if (((leftRegister == Register.AH || leftRegister == Register.AL) && (rightRegister != Register.AH && rightRegister != Register.AL)) ||
                                    ((rightRegister == Register.AH || rightRegister == Register.AL) && (leftRegister != Register.AH && leftRegister != Register.AL)))
                                    return ParseState.UNMATCHED_OPERANDS;

                                output.Write((byte)leftRegister);
                                output.Write((byte)rightRegister);
                                binaryFileLength += 3;

                                break;
                            }
                        case "ADD":
                            {
                                var leftRegister = getRegister(leftOperand);
                                if (leftRegister == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                if (rightOperand.StartsWith("#") || rightOperand.StartsWith("$"))       //ADD R V
                                {
                                    output.Write((byte)0x11);

                                    var rightValue = getWordValue(rightOperand);
                                    if (!rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;

                                    output.Write((byte)leftRegister);
                                    output.Write((UInt16)rightValue);
                                    binaryFileLength += 4;
                                }
                                else        //ADD R R
                                {
                                    output.Write((byte)0x12);

                                    var rightRegister = getRegister(rightOperand);

                                    if (rightRegister == Register.UNKNOWN)
                                        return ParseState.UNKNOWN_REGISTER;

                                    if (((leftRegister == Register.AH || leftRegister == Register.AL) && (rightRegister != Register.AH && rightRegister != Register.AL)) ||
                                        ((rightRegister == Register.AH || rightRegister == Register.AL) && (leftRegister != Register.AH && leftRegister != Register.AL)))
                                        return ParseState.UNMATCHED_OPERANDS;

                                    output.Write((byte)leftRegister);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 3;
                                }
                                break;
                            }
                        case "SUB":
                            {
                                var leftRegister = getRegister(leftOperand);
                                if (leftRegister == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                if (rightOperand.StartsWith("#") || rightOperand.StartsWith("#"))       //SUB R V
                                {
                                    output.Write((byte)0x13);

                                    var rightValue = getWordValue(rightOperand);
                                    if (!rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;

                                    output.Write((byte)leftRegister);
                                    output.Write((UInt16)rightValue);
                                    binaryFileLength += 4;
                                }
                                else        //SUB R R
                                {
                                    output.Write((byte)0x14);

                                    var rightRegister = getRegister(rightOperand);

                                    if (rightRegister == Register.UNKNOWN)
                                        return ParseState.UNKNOWN_REGISTER;

                                    if (((leftRegister == Register.AH || leftRegister == Register.AL) && (rightRegister != Register.AH && rightRegister != Register.AL)) ||
                                        ((rightRegister == Register.AH || rightRegister == Register.AL) && (leftRegister != Register.AH && leftRegister != Register.AL)))
                                        return ParseState.UNMATCHED_OPERANDS;

                                    output.Write((byte)leftRegister);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 3;
                                }
                                break;
                            }
                        case "MUL":
                            {
                                var leftRegister = getRegister(leftOperand);
                                if (leftRegister == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                if (rightOperand.StartsWith("#") || rightOperand.StartsWith("$"))       //MUL R V
                                {
                                    output.Write((byte)0x15);

                                    var rightValue = getWordValue(rightOperand);
                                    if (!rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;

                                    output.Write((byte)leftRegister);
                                    output.Write((UInt16)rightValue);
                                    binaryFileLength += 4;
                                }
                                else        //MUL R R
                                {
                                    output.Write((byte)0x16);

                                    var rightRegister = getRegister(rightOperand);

                                    if (rightRegister == Register.UNKNOWN)
                                        return ParseState.UNKNOWN_REGISTER;

                                    output.Write((byte)leftRegister);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 3;
                                }
                                break;
                            }
                        case "DIV":
                            {
                                var leftRegister = getRegister(leftOperand);
                                if (leftRegister == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                if (rightOperand.StartsWith("#") || rightOperand.StartsWith("$"))       //DIV R V
                                {
                                    output.Write((byte)0x17);

                                    var rightValue = getWordValue(rightOperand);
                                    if (!rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;

                                    output.Write((byte)leftRegister);
                                    output.Write((UInt16)rightValue);
                                    binaryFileLength += 4;
                                }
                                else        //DIV R R
                                {
                                    output.Write((byte)0x18);

                                    var rightRegister = getRegister(rightOperand);

                                    if (rightRegister == Register.UNKNOWN)
                                        return ParseState.UNKNOWN_REGISTER;

                                    output.Write((byte)leftRegister);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 3;
                                }
                                break;
                            }
                        case "SHL":
                            {
                                output.Write((byte)0x19);

                                var register = getRegister(leftOperand);
                                if (register == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                var rightValue = getWordValue(rightOperand);
                                if (!rightValue.HasValue)
                                    return ParseState.INCORRECT_VALUE;

                                output.Write((byte)register);
                                output.Write((UInt16)rightValue);
                                binaryFileLength += 4;
                                break;
                            }
                        case "SHR":
                            {
                                output.Write((byte)0x1a);

                                var register = getRegister(leftOperand);
                                if (register == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                var rightValue = getWordValue(rightOperand);
                                if (!rightValue.HasValue)
                                    return ParseState.INCORRECT_VALUE;

                                output.Write((byte)register);
                                output.Write((UInt16)rightValue);
                                binaryFileLength += 4;
                                break;
                            }
                        case "ADC":
                            {
                                var leftRegister = getRegister(leftOperand);
                                if (leftRegister == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                if (rightOperand.StartsWith("#") || rightOperand.StartsWith("$"))       //ADC R V
                                {
                                    output.Write((byte)0x1b);

                                    var rightValue = getWordValue(rightOperand);
                                    if (!rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;

                                    output.Write((byte)leftRegister);
                                    output.Write((UInt16)rightValue);
                                    binaryFileLength += 4;
                                }
                                else        //ADC R R
                                {
                                    output.Write((byte)0x1c);

                                    var rightRegister = getRegister(rightOperand);

                                    if (rightRegister == Register.UNKNOWN)
                                        return ParseState.UNKNOWN_REGISTER;

                                    if (((leftRegister == Register.AH || leftRegister == Register.AL) && (rightRegister != Register.AH && rightRegister != Register.AL)) ||
                                        ((rightRegister == Register.AH || rightRegister == Register.AL) && (leftRegister != Register.AH && leftRegister != Register.AL)))
                                        return ParseState.UNMATCHED_OPERANDS;

                                    output.Write((byte)leftRegister);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 3;
                                }
                                break;
                            }
                        case "SBB":
                            {
                                var leftRegister = getRegister(leftOperand);
                                if (leftRegister == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                if (rightOperand.StartsWith("#") || rightOperand.StartsWith("$"))       //SBB R V
                                {
                                    output.Write((byte)0x1d);

                                    var rightValue = getWordValue(rightOperand);
                                    if (!rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;

                                    output.Write((byte)leftRegister);
                                    output.Write((UInt16)rightValue);
                                    binaryFileLength += 4;
                                }
                                else        //SBB R R
                                {
                                    output.Write((byte)0x1e);

                                    var rightRegister = getRegister(rightOperand);

                                    if (rightRegister == Register.UNKNOWN)
                                        return ParseState.UNKNOWN_REGISTER;

                                    if (((leftRegister == Register.AH || leftRegister == Register.AL) && (rightRegister != Register.AH && rightRegister != Register.AL)) ||
                                        ((rightRegister == Register.AH || rightRegister == Register.AL) && (leftRegister != Register.AH && leftRegister != Register.AL)))
                                        return ParseState.UNMATCHED_OPERANDS;

                                    output.Write((byte)leftRegister);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 3;
                                }
                                break;
                            }
                        case "AND":
                            {
                                var leftRegister = getRegister(leftOperand);

                                if (rightOperand.StartsWith("#") || rightOperand.StartsWith("$"))       //AND R V
                                {
                                    output.Write((byte)0x1f);

                                    var rightValue = getWordValue(rightOperand);
                                    if (!rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;

                                    if ((leftRegister == Register.AH || leftRegister == Register.AL) && rightValue > 0xff)
                                        return ParseState.UNMATCHED_OPERANDS;

                                    output.Write((byte)leftRegister);
                                    output.Write((UInt16)rightValue);
                                    binaryFileLength += 4;
                                }
                                else        //AND R R
                                {
                                    output.Write((byte)0x24);
                                    var rightRegister = getRegister(rightOperand);

                                    if (leftRegister == Register.UNKNOWN || rightRegister == Register.UNKNOWN)
                                        return ParseState.UNKNOWN_REGISTER;

                                    if (((leftRegister == Register.AH || leftRegister == Register.AL) && (rightRegister != Register.AH && rightRegister != Register.AL)) ||
                                        ((rightRegister == Register.AH || rightRegister == Register.AL) && (leftRegister != Register.AH && leftRegister != Register.AL)))
                                        return ParseState.UNMATCHED_OPERANDS;

                                    output.Write((byte)leftRegister);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 3;
                                }
                                break;
                            }
                        case "OR":
                            {
                                var leftRegister = getRegister(leftOperand);

                                if (rightOperand.StartsWith("#") || rightOperand.StartsWith("$"))
                                {
                                    output.Write((byte)0x20);

                                    var rightValue = getWordValue(rightOperand);
                                    if (!rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;

                                    if ((leftRegister == Register.AH || leftRegister == Register.AL) && rightValue > 0xff)
                                        return ParseState.UNMATCHED_OPERANDS;

                                    output.Write((byte)leftRegister);
                                    output.Write((UInt16)rightValue);
                                    binaryFileLength += 4;
                                }
                                else
                                {
                                    output.Write((byte)0x25);

                                    var rightRegister = getRegister(rightOperand);

                                    if (leftRegister == Register.UNKNOWN || rightRegister == Register.UNKNOWN)
                                        return ParseState.UNKNOWN_REGISTER;

                                    if (((leftRegister == Register.AH || leftRegister == Register.AL) && (rightRegister != Register.AH && rightRegister != Register.AL)) ||
                                        ((rightRegister == Register.AH || rightRegister == Register.AL) && (leftRegister != Register.AH && leftRegister != Register.AL)))
                                        return ParseState.UNMATCHED_OPERANDS;

                                    output.Write((byte)leftRegister);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 3;
                                }
                                break;
                            }
                        case "XOR":
                            {
                                var leftRegister = getRegister(leftOperand);

                                if (rightOperand.StartsWith("#") || rightOperand.StartsWith("#"))
                                {
                                    output.Write((byte)(0x21));

                                    var rightValue = getWordValue(rightOperand);
                                    if (!rightValue.HasValue)
                                        return ParseState.INCORRECT_VALUE;

                                    if ((leftRegister == Register.AH || leftRegister == Register.AL) && rightValue > 0xff)
                                        return ParseState.UNMATCHED_OPERANDS;

                                    output.Write((byte)leftRegister);
                                    output.Write((UInt16)rightValue);
                                    binaryFileLength += 4;
                                }
                                else
                                {
                                    output.Write((byte)(0x26));

                                    var rightRegister = getRegister(rightOperand);

                                    if (leftRegister == Register.UNKNOWN || rightRegister == Register.UNKNOWN)
                                        return ParseState.UNKNOWN_REGISTER;

                                    if (((leftRegister == Register.AH || leftRegister == Register.AL) && (rightRegister != Register.AH && rightRegister != Register.AL)) ||
                                        ((rightRegister == Register.AH || rightRegister == Register.AL) && (leftRegister != Register.AH && leftRegister != Register.AL)))
                                        return ParseState.UNMATCHED_OPERANDS;

                                    output.Write((byte)leftRegister);
                                    output.Write((byte)rightRegister);
                                    binaryFileLength += 3;
                                }
                                break;
                            }
                        case "RCL":
                            {
                                output.Write((byte)0x22);

                                var register = getRegister(leftOperand);
                                if (register == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                var rightValue = getWordValue(rightOperand);
                                if (!rightValue.HasValue)
                                    return ParseState.INCORRECT_VALUE;

                                output.Write((byte)register);
                                output.Write((UInt16)rightValue);
                                binaryFileLength += 4;
                                break;
                            }
                        case "RCR":
                            {
                                output.Write((byte)0x23);

                                var register = getRegister(leftOperand);
                                if (register == Register.UNKNOWN)
                                    return ParseState.UNKNOWN_REGISTER;

                                var rightValue = getWordValue(rightOperand);
                                if (!rightValue.HasValue)
                                    return ParseState.INCORRECT_VALUE;

                                output.Write((byte)register);
                                output.Write((UInt16)rightValue);
                                binaryFileLength += 4;
                                break;
                            }
                        default:
                            return ParseState.ILLEGAL_INSTRUCTION;
                    }
                    return ParseState.SUCCESSFUL;
                }
            }
        }

        private void cleanLine(ref string line)
        {
            try
            {
                line = line.Substring(0, line.IndexOf('!'));
                line = line.Trim();
            }
            catch
            {
                line = line.Trim();
            }
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
                case "D":
                    return Register.D;
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

                try
                {
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
                catch
                {
                    return ret;
                }
            }
            else
            {
                if (operand.StartsWith("$"))
                {
                    if (operand.Length > 2)
                        return ret;
                    else
                    {
                        ret = (byte)operand[1];
                        return ret;
                    }
                }
                else
                    return ret;
            }
        }

        private UInt16? getWordValue(string operand)
        {
            UInt16? ret = null;
            if (operand.StartsWith("#"))
            {
                operand = operand.Remove(0, 1);
                char last = operand[operand.Length - 1];

                try
                {
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
                catch
                {
                    return ret;
                }
            }
            else
            if (operand.StartsWith("$"))
            {
                if (operand.Length > 2)
                    return ret;
                else
                {
                    ret = (UInt16)operand[1];
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
