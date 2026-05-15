using BetterFileSys.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BetterFileSys.Services
{
    /// <summary>
    /// Service for file system operations
    /// </summary>
    public class FileSystemService
    {
        public async Task<List<FileItem>> GetDirectoryContentsAsync(string path)
        {
            return await Task.Run(() =>
            {
                var items = new List<FileItem>();

                try
                {
                    var directoryInfo = new DirectoryInfo(path);

                    // Get directories
                    foreach (var dir in directoryInfo.GetDirectories())
                    {
                        items.Add(new FileItem
                        {
                            Name = dir.Name,
                            Path = dir.FullName,
                            IsDirectory = true,
                            Modified = dir.LastWriteTime,
                            FileType = "Folder"
                        });
                    }

                    // Get files
                    foreach (var file in directoryInfo.GetFiles())
                    {
                        items.Add(new FileItem
                        {
                            Name = file.Name,
                            Path = file.FullName,
                            IsDirectory = false,
                            Size = file.Length,
                            Modified = file.LastWriteTime,
                            FileType = file.Extension
                        });
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Handle permission denied
                }
                catch (DirectoryNotFoundException)
                {
                    // Handle directory not found
                }

                return items.OrderByDescending(x => x.IsDirectory).ToList();
            });
        }

        public async Task<bool> DeleteFileAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Log error
                }
                return false;
            });
        }

        public async Task<bool> CreateFolderAsync(string parentPath, string folderName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string newPath = Path.Combine(parentPath, folderName);
                    Directory.CreateDirectory(newPath);
                    return true;
                }
                catch (Exception)
                {
                    // Log error
                }
                return false;
            });
        }
    }
}
