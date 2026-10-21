using System;
using System.IO;
using System.Text;

namespace SourceGenBuilder.Editor
{
    /// <summary>
    /// A simple file-backed log with single-file rotation.
    /// On <see cref="Rotate"/>, current → .prev, then current is cleared.
    /// Thread-safe for appending.
    /// </summary>
    public class RotatingFileLog
    {
        private readonly string _logPath;
        private readonly string _prevLogPath;
        private readonly object _writeLock = new();
        private readonly StringBuilder _buffer = new();

        public RotatingFileLog(string folderPath, string logFileName, string prevLogFileName)
        {
            Directory.CreateDirectory(folderPath);
            _logPath = Path.Combine(folderPath, logFileName);
            _prevLogPath = Path.Combine(folderPath, prevLogFileName);
        }

        /// <summary>
        /// In-memory buffer contents.
        /// </summary>
        public string Content
        {
            get
            {
                lock (_writeLock)
                {
                    return _buffer.ToString();
                }
            }
        }

        /// <summary>
        /// Loads the current log file from disk into the in-memory buffer.
        /// </summary>
        public void LoadFromDisk()
        {
            lock (_writeLock)
            {
                _buffer.Clear();
                if (File.Exists(_logPath))
                {
                    _buffer.Append(File.ReadAllText(_logPath));
                }
            }
        }

        /// <summary>
        /// Replaces the in-memory buffer with the given content and overwrites the file.
        /// </summary>
        public void SetContent(string content)
        {
            lock (_writeLock)
            {
                _buffer.Clear();
                _buffer.Append(content);
                File.WriteAllText(_logPath, content);
            }
        }

        /// <summary>
        /// Rotates: current → .prev, then clears both the file and in-memory buffer.
        /// </summary>
        public void Rotate()
        {
            lock (_writeLock)
            {
                if (File.Exists(_logPath))
                {
                    File.Copy(_logPath, _prevLogPath, overwrite: true);
                }

                File.WriteAllText(_logPath, "");
                _buffer.Clear();
            }
        }

        /// <summary>
        /// Clears the in-memory buffer and the log file on disk.
        /// </summary>
        public void Clear()
        {
            lock (_writeLock)
            {
                _buffer.Clear();
                File.WriteAllText(_logPath, "");
            }
        }

        /// <summary>
        /// Appends a timestamped line to both the in-memory buffer and disk.
        /// </summary>
        public void AppendTimestamped(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
            Append(line);
        }

        /// <summary>
        /// Appends raw text (no timestamp) to both the in-memory buffer and disk.
        /// </summary>
        public void Append(string text)
        {
            lock (_writeLock)
            {
                _buffer.Append(text);
                File.AppendAllText(_logPath, text);
            }
        }
    }
}
