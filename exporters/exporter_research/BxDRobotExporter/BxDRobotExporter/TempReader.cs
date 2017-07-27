﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Inventor;

namespace ExportProcess
{
    public class TempReader
    {
        #region State Variables
        private int ID;
        private AssemblyDocument currentDocument;
        //path to file
        private string directoryPath = "C:\\Users\\" + System.Environment.UserName + "\\AppData\\Roaming\\Autodesk\\Synthesis\\";
        #endregion
        public TempReader(AssemblyDocument currentDocument)
        {
            ID = -1;
            this.currentDocument = currentDocument;
            if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
        }

        private byte[] ReadBMP(string fileName)
        {
            try
            {
                //path to file
                string file = directoryPath + fileName;
                //byte array that will be returned
                byte[] fileBytes;

                //reads the file into the array
                fileBytes = System.IO.File.ReadAllBytes(file);

                //returns
                return fileBytes;
            }
            catch (Exception e)
            {
                //catches problems
                MessageBox.Show(e.Message);
            }
            //returns in case of problem
            return null;
        }
        private STLData ReadSTL(string fileName)
        {
            try
            {
                string path = directoryPath + fileName;
                //byte array that will be returned
                byte[] fileBytes;
                //reads the file into the array
                fileBytes = System.IO.File.ReadAllBytes(path);
                fileName = fileName.Replace(".stl", "");
                ID++;
                float[,] trans = new float[4, 4];
                foreach (ComponentOccurrence component in currentDocument.ComponentDefinition.Occurrences)
                {
                    if (fileName.Replace("\\", "").Equals(NameFilter(component.Name)))
                    {
                        Matrix m = component.Transformation;
                        for (int x = 1; x < 5; x++)
                        {
                            for (int y = 1; y < 5; y++)
                            {
                                trans[x, y] = (float)m.Cell[x, y];
                            }
                        }
                    }
                }
                return new STLData(ID, fileBytes, trans);
            }
            catch (Exception e)
            {
                //catches problems
                MessageBox.Show(e.Message + "\n\n\n" + e.StackTrace);
            }
            //returns in case of problem
            return new STLData();
        }
        public byte[] ReadFiles()
        {
            try
            {
                bool firstStl = true;
                List<byte> bytesOfFiles = new List<byte>(), bmpBytes = new List<byte>();
                string updatedFile;
                uint numOfStls = 0;
                foreach (string file in Directory.GetFiles(directoryPath))
                {
                    updatedFile = file.Substring(file.LastIndexOf("\\")+1, file.Length - file.Substring(0, file.LastIndexOf("\\")).Length-1);
                    if (updatedFile.Contains(".bmp"))
                    {
                        foreach (byte byteID in BitConverter.GetBytes(0000))
                        {
                            bmpBytes.Add(byteID);
                        }
                        byte[] BMPBytes = ReadBMP(updatedFile);
                        foreach (byte byteLength in BitConverter.GetBytes(BMPBytes.Length))
                        {
                            bmpBytes.Add(byteLength);
                        }
                        foreach (byte bmpSec in BMPBytes)
                        {
                            bmpBytes.Add(bmpSec);
                        }
                    }

                    else if (updatedFile.Contains(".stl"))
                    {
                        numOfStls++;
                        if (firstStl)
                        {
                            foreach (byte byteID in BitConverter.GetBytes(0001))
                            {
                                bytesOfFiles.Add(byteID);
                            }
                            firstStl = false;
                        }
                            byte[] stlBytes = ReadSTL(updatedFile).getData();
                        foreach (byte byteLength in BitConverter.GetBytes(stlBytes.Length))
                        {
                            bytesOfFiles.Add(byteLength);
                        }
                        foreach (byte stlSec in stlBytes)
                        {
                            bytesOfFiles.Add(stlSec);
                        }
                    }
                    System.IO.File.Delete(file);
                }
                byte[] numOfStlBytes = BitConverter.GetBytes(numOfStls);
                for (int lengthBytes = 0; lengthBytes < numOfStlBytes.Length; lengthBytes++)
                {
                    bytesOfFiles.Insert(lengthBytes, numOfStlBytes[lengthBytes]);
                }
                foreach (byte bmpByte in bmpBytes)
                {
                    bytesOfFiles.Add(bmpByte);
                }
                return bytesOfFiles.ToArray();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return null;
            }
        }
        public Dictionary<string, STLData> GetSTLDict()
        {

            Dictionary<string, STLData> output = new Dictionary<string, STLData>();
            string[] paths = Directory.GetFiles(directoryPath);

            foreach (string path in paths)
            {
                if (path.Contains(".stl"))
                {
                    string name = path.Substring(path.LastIndexOf("\\") + 1, path.IndexOf(".") - 1 - (path.LastIndexOf("\\")));
                    output.Add(name, ReadSTL(name + ".stl"));
                }
            }
            return output;
        }
        private string NameFilter(string name)
        {
            //each line removes an invalid character from the file name 
            name = name.Replace("\\", "");
            name = name.Replace("/", "");
            name = name.Replace("*", "");
            name = name.Replace("?", "");
            name = name.Replace("\"", "");
            name = name.Replace("<", "");
            name = name.Replace(">", "");
            name = name.Replace("|", "");
            return name;
        }
    }
}
