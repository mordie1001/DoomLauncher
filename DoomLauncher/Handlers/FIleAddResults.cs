﻿using System.Collections.Generic;

namespace DoomLauncher
{
    public class FileError
    {
        public string FileName { get; set; }
        public string Error { get; set; }
    }

    class FileAddResults
    {
        public List<string> NewFiles = new List<string>();
        public List<string> ReplacedFiles = new List<string>();
        public List<FileError> Errors = new List<FileError>();
    }
}
