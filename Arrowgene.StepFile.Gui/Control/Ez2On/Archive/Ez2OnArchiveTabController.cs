﻿using Arrowgene.Services.Extra;
using Arrowgene.StepFile.Control.ArchiveTab;
using Arrowgene.StepFile.Core;
using Arrowgene.StepFile.Core.DynamicGridView;
using Arrowgene.StepFile.Core.Ez2On.Archive;
using Arrowgene.StepFile.Plugin;
using Arrowgene.StepFile.Windows.SelectOption;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Arrowgene.StepFile.Control.Ez2On.Archive
{
    /// <summary>
    /// 2) File Preview
    /// 3) Warn unsaved progress
    /// 4) Rename directory / file
    /// 5) New directory
    /// </summary>
    public class Ez2OnArchiveTabController : ArchiveTabController
    {

        private Ez2OnArchive _archive;
        private Ez2OnArchiveTabFolder _root;
        private Ez2OnArchiveTabFolder _currentFolder;
        private Ez2OnArchiveTabFile _currentFile;
        private ArchiveTabItem _currentSelection;
        private Ez2OnArchiveTabControl _ez2OnArchiveTabControl;
        private ICollection<Ez2OnArchiveCrypto> _cryptos;
        private byte[] _crypoKey;

        private CommandHandler _cmdKeyAdd;
        private CommandHandler _cmdAdd;
        private CommandHandler _cmdKeyDelete;
        private CommandHandler _cmdExtract;
        private CommandHandler _cmdDelete;

        public Ez2OnArchiveTabController() : base(new Ez2OnArchiveTabControl())
        {
            _cryptos = new List<Ez2OnArchiveCrypto>();
            _ez2OnArchiveTabControl = TabUserControl as Ez2OnArchiveTabControl;
            Header = "Ez2On Archive";

            LoadArchiveCryptos();

            _ez2OnArchiveTabControl.AddColumn(new DynamicGridViewColumn { Header = "ItemName", ContentField = "ItemName" });
            _ez2OnArchiveTabControl.AddColumn(new DynamicGridViewColumn { Header = "Name", TextField = "Name" });
            _ez2OnArchiveTabControl.AddColumn(new DynamicGridViewColumn { Header = "Extension", TextField = "Extension" });
            _ez2OnArchiveTabControl.AddColumn(new DynamicGridViewColumn { Header = "DirectoryPath", TextField = "DirectoryPath" });
            _ez2OnArchiveTabControl.AddColumn(new DynamicGridViewColumn { Header = "FullPath", TextField = "FullPath" });
            _ez2OnArchiveTabControl.AddColumn(new DynamicGridViewColumn { Header = "Offset", TextField = "Offset" });
            _ez2OnArchiveTabControl.AddColumn(new DynamicGridViewColumn { Header = "Length", TextField = "Length" });
            _ez2OnArchiveTabControl.AddColumn(new DynamicGridViewColumn { Header = "Encrypted", TextField = "Encrypted" });

            _ez2OnArchiveTabControl.OpenCommand = new CommandHandler(Open, true);
            _ez2OnArchiveTabControl.SaveCommand = new CommandHandler(Save, true);
            _ez2OnArchiveTabControl.ExtractCommand = _cmdExtract = new CommandHandler(Extract, CanExtract);
            _ez2OnArchiveTabControl.DeleteCommand = _cmdDelete = new CommandHandler(Delete, CanDelete);
            _ez2OnArchiveTabControl.AddCommand = _cmdAdd = new CommandHandler(Add, CanAdd);
            _ez2OnArchiveTabControl.KeyAddCommand = _cmdKeyAdd = new CommandHandler(KeyAdd, CanAddKey);
            _ez2OnArchiveTabControl.KeyDeleteCommand = _cmdKeyDelete = new CommandHandler(KeyDelete, CanDeleteKey);
            _ez2OnArchiveTabControl.KeyGenerateCommand = new CommandHandler(KeyGenerate, true);
            _ez2OnArchiveTabControl.EncryptBatchCommand = new CommandHandler(EncryptBatch, true);
            _ez2OnArchiveTabControl.DecryptBatchCommend = new CommandHandler(DecryptBatch, true);


            _ez2OnArchiveTabControl.ListViewItems.MouseDoubleClick += ListViewItems_MouseDoubleClick;
            _ez2OnArchiveTabControl.ListViewItems.SelectionChanged += ListViewItems_SelectionChanged;
            _ez2OnArchiveTabControl.ListViewItems.AllowDrop = true;
            _ez2OnArchiveTabControl.ListViewItems.Drop += ListViewItems_Drop;

            _archive = new Ez2OnArchive();
            _root = new Ez2OnArchiveTabFolder(_archive.RootFolder);
            _currentFolder = _root;

            _cmdAdd.RaiseCanExecuteChanged();
            _cmdKeyAdd.RaiseCanExecuteChanged();
            _cmdKeyDelete.RaiseCanExecuteChanged();
            _cmdExtract.RaiseCanExecuteChanged();
            _cmdDelete.RaiseCanExecuteChanged();
        }

        private async void Open()
        {
            FileInfo selected = new SelectFileBuilder()
                .Filter("Ez2On Archive files(*.dat, *.tro) | *.dat; *.tro")
                .SelectSingle();
            if (selected == null)
            {
                return;
            }
            Header = selected.Name;
            Ez2OnArchiveIO archiveIO = new Ez2OnArchiveIO();
            archiveIO.ProgressChanged += ArchiveIO_ProgressChanged;
            var task = Task.Run(() =>
             {
                 Ez2OnArchive archive = archiveIO.Read(selected.FullName);
                 Ez2OnArchiveTabFolder root = new Ez2OnArchiveTabFolder(archive.RootFolder);
                 return new Tuple<Ez2OnArchive, Ez2OnArchiveTabFolder>(archive, root);
             });
            Tuple<Ez2OnArchive, Ez2OnArchiveTabFolder> result = await task;
            _archive = result.Item1;
            _root = result.Item2;
            _currentFolder = _root;
            if (_archive.CryptoType == Ez2OnArchive.CRYPTO_TYPE_NONE)
            {
                _ez2OnArchiveTabControl.Encryption = "None";
            }
            else
            {
                Ez2OnArchiveCrypto activeCrypto = GetArchiveCrypto(_archive.CryptoType);
                _ez2OnArchiveTabControl.Encryption = activeCrypto == null ? "Unknown" : activeCrypto.Name;
            }
            _crypoKey = null;
            _ez2OnArchiveTabControl.ArchiveType = _archive.ArchiveType;
            _ez2OnArchiveTabControl.ClearItems();
            _ez2OnArchiveTabControl.AddItemRange(_root.Folders);
            _ez2OnArchiveTabControl.AddItemRange(_root.Files);
            _cmdAdd.RaiseCanExecuteChanged();
            _cmdKeyAdd.RaiseCanExecuteChanged();
            _cmdKeyDelete.RaiseCanExecuteChanged();
            _cmdExtract.RaiseCanExecuteChanged();
            _cmdDelete.RaiseCanExecuteChanged();
            App.ResetProgress(this);

        }

        private async void Save()
        {
            FileInfo selected = new SaveFileBuilder()
                .Filter("Ez2On Data Archive (.tro)|*.tro|Ez2On Music Archive (.dat)|*.dat")
                .Select();
            if (selected == null)
            {
                return;
            }
            Ez2OnArchiveIO archiveIO = new Ez2OnArchiveIO();
            archiveIO.ProgressChanged += ArchiveIO_ProgressChanged;
            var task = Task.Run(() =>
            {
                archiveIO.Write(_archive, selected.FullName);
            });
            await task;
            App.ResetProgress(this);
        }

        private void Add()
        {
            Ez2OnArchiveCrypto activeCrypto = null;
            if (_archive.CryptoType != Ez2OnArchive.CRYPTO_TYPE_NONE)
            {
                activeCrypto = GetArchiveCrypto(_archive.CryptoType);
                if (activeCrypto == null)
                {
                    MessageBox.Show($"Can not add file. Archive is encrypted with an unknown encryption", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (!activeCrypto.CanEncrypt)
                {
                    MessageBox.Show($"Can not add file. Encryption is not supported for '{activeCrypto.Name}'", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            FileInfo selected = new SelectFileBuilder()
                .Filter("Add files(*.*) | *.*")
                .SelectSingle();
            if (selected == null)
            {
                return;
            }
            Ez2OnArchiveTabFile tabFile = CreateFile(selected, activeCrypto, _currentFolder);
            if (tabFile == null)
            {
                return;
            }
            _archive.Files.Add(tabFile.File);
            _currentFolder.AddFile(tabFile);
            _ez2OnArchiveTabControl.Items.Add(tabFile);
        }

        private bool CanAdd()
        {
            if (_archive == null)
            {
                return false;
            }
            //  if (_archive.CryptoType == Ez2OnArchiveCryptoType.AesCrypto && _activeCrypto == null)
            //  {
            //      return false;
            //  }
            //  if (_archive.CryptoType == Ez2OnArchiveCryptoType.EzCrypto)
            //  {
            //      return false;
            //  }
            return true;
        }

        private void Delete()
        {
            if (_currentFile != null)
            {
                _currentFolder.RemoveFile(_currentFile);
                _archive.Files.Remove(_currentFile.File);
                _ez2OnArchiveTabControl.Items.Remove(_currentFile);
                _currentFile = null;
            }
        }

        private bool CanDelete()
        {
            return _currentFile != null;
        }

        private async void Extract()
        {
            DirectoryInfo selected = new SelectFolderBuilder()
                .Select();
            if (selected == null)
            {
                return;
            }
            if (_currentSelection is Ez2OnArchiveTabFolder)
            {
                Ez2OnArchiveTabFolder folder = _currentSelection as Ez2OnArchiveTabFolder;
                Ez2OnArchiveIO archiveIO = new Ez2OnArchiveIO();
                archiveIO.ProgressChanged += ArchiveIO_ProgressChanged;
                var task = Task.Run(() =>
                {
                    archiveIO.WriteFolder(folder.Folder, selected.FullName);
                });
                await task;
                App.ResetProgress(this);
            }
            else if (_currentSelection is Ez2OnArchiveTabFile)
            {
                Ez2OnArchiveTabFile file = _currentSelection as Ez2OnArchiveTabFile;
                string path = Path.Combine(selected.FullName, file.Name);
                Utils.WriteFile(file.File.Data, path);
            }
            App.ResetProgress(this);
        }

        private bool CanExtract()
        {
            return _currentSelection != null && _currentFolder != null && _currentSelection != _currentFolder.Parent;
        }

        private async void KeyAdd()
        {
            Ez2OnArchiveCrypto activeCrypto = null;
            if (_archive.CryptoType != Ez2OnArchive.CRYPTO_TYPE_NONE)
            {
                activeCrypto = GetArchiveCrypto(_archive.CryptoType);
                if (activeCrypto == null)
                {
                    MessageBox.Show($"Can not encrypt archive. Archive is encrypted with an unknown encryption", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                else
                {
                    MessageBox.Show($"Can not encrypt archive. Archive is encrypted with '{activeCrypto.Name}' encryption", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            SelectOptionBuilder<Ez2OnArchiveCrypto> selectOption = new SelectOptionBuilder<Ez2OnArchiveCrypto>()
                .SetTitle("Select Crypto")
                .SetText("Choose a crypto to apply");
            bool hasCrypto = false;
            foreach (Ez2OnArchiveCrypto crypto in _cryptos)
            {
                if (crypto.CanEncrypt)
                {
                    selectOption.AddOption(crypto, crypto.Name);
                    hasCrypto = true;
                }
            }
            if (!hasCrypto)
            {
                MessageBox.Show($"Can not encrypt archive. No suitable encryption plugin available", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Ez2OnArchiveCrypto selectedCrypto = selectOption.Select();
            if (selectedCrypto == null)
            {
                return;
            }
            if (selectedCrypto.CryptoPlugin is ICryptoPlugin)
            {
                ICryptoPlugin selectedICryptoPlugin = (ICryptoPlugin)selectedCrypto.CryptoPlugin;
                FileInfo selected = new SelectFileBuilder()
                    .Filter("Ez2On Archive Key file(*.key) | *.key")
                    .SelectSingle();
                if (selected == null)
                {
                    return;
                }
                _crypoKey = Utils.ReadFile(selected.FullName);
                selectedICryptoPlugin.SetKey(_crypoKey);
            }
            int total = _archive.Files.Count;
            int current = 0;
            var task = Task.Run(() =>
            {
                foreach (Ez2OnArchiveFile file in _archive.Files)
                {
                    float progress = current++ / (float)total * 100;
                    App.UpdateProgress(this, (int)progress, $"Encrypting: {file.FullPath}");
                    selectedCrypto.Encrypt(file);
                }
            });
            await task;
            _archive.CryptoType = selectedCrypto.CryptoType;
            _ez2OnArchiveTabControl.Encryption = selectedCrypto.Name;
            _cmdKeyAdd.RaiseCanExecuteChanged();
            _cmdKeyDelete.RaiseCanExecuteChanged();
            _cmdAdd.RaiseCanExecuteChanged();
            App.ResetProgress(this);
            MessageBox.Show($"Added '{selectedCrypto.Name}' encryption", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private bool CanAddKey()
        {
            return true;
            //   if (_archive == null)
            //   {
            //       return false;
            //   }
            //   if (_activeCrypto == null && _archive.CryptoType == Ez2OnArchiveCryptoType.AesCrypto)
            //   {
            //       return true;
            //   }
            //   if (_activeCrypto == null && _archive.CryptoType == Ez2OnArchiveCryptoType.None)
            //   {
            //       return true;
            //   }
            //   return false;
        }

        private async void KeyDelete()
        {
            if (_archive.CryptoType == Ez2OnArchive.CRYPTO_TYPE_NONE)
            {
                MessageBox.Show($"Can not remove encryption. Archive is not encrypted", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Ez2OnArchiveCrypto activeCrypto = GetArchiveCrypto(_archive.CryptoType);
            if (activeCrypto == null)
            {
                MessageBox.Show($"Can not remove encryption. Archive is encrypted with an unknown encryption", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!activeCrypto.CanDecrypt)
            {
                MessageBox.Show($"Can not remove encryption. '{activeCrypto.Name}' does not support decryption", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (activeCrypto.CryptoPlugin is ICryptoPlugin)
            {
                ICryptoPlugin selectedICryptoPlugin = (ICryptoPlugin)activeCrypto.CryptoPlugin;
                if (_crypoKey == null)
                {
                    FileInfo selected = new SelectFileBuilder()
                        .Filter("Ez2On Archive Key file(*.key) | *.key")
                        .SelectSingle();
                    if (selected == null)
                    {
                        return;
                    }
                    _crypoKey = Utils.ReadFile(selected.FullName);
                }
                selectedICryptoPlugin.SetKey(_crypoKey);
            }
            int total = _archive.Files.Count;
            int current = 0;
            var task = Task.Run(() =>
            {
                foreach (Ez2OnArchiveFile file in _archive.Files)
                {
                    float progress = current++ / (float)total * 100;
                    App.UpdateProgress(this, (int)progress, $"Decrypting: {file.FullPath}");
                    activeCrypto.Decrypt(file);
                }
            });
            await task;
            _archive.CryptoType = Ez2OnArchive.CRYPTO_TYPE_NONE;
            _ez2OnArchiveTabControl.Encryption = "None";
            _crypoKey = null;
            _cmdKeyAdd.RaiseCanExecuteChanged();
            _cmdKeyDelete.RaiseCanExecuteChanged();
            _cmdAdd.RaiseCanExecuteChanged();
            App.ResetProgress(this);
            MessageBox.Show($"Removed '{activeCrypto.Name}' Encryption", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private bool CanDeleteKey()
        {
            return true;
            //   if (_archive == null)
            //   {
            //       return false;
            //   }
            //   if (_activeCrypto == null && _archive.CryptoType == Ez2OnArchiveCryptoType.EzCrypto)
            //   {
            //       return true;
            //   }
            //   if (_activeCrypto != null && _archive.CryptoType == Ez2OnArchiveCryptoType.AesCrypto)
            //   {
            //       return true;
            //   }
            //   return false;
        }

        private void KeyGenerate()
        {
            SelectOptionBuilder<ICryptoPlugin> selectOption = new SelectOptionBuilder<ICryptoPlugin>()
                .SetTitle("Select Crypto")
                .SetText("Choose a crypto to generate a key");
            bool hasCrypto = false;
            foreach (Ez2OnArchiveCrypto crypto in _cryptos)
            {
                if (crypto.CryptoPlugin is ICryptoPlugin)
                {
                    selectOption.AddOption((ICryptoPlugin)crypto.CryptoPlugin, crypto.Name);
                    hasCrypto = true;
                }
            }
            if (!hasCrypto)
            {
                MessageBox.Show($"No Crypto with key generation available", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ICryptoPlugin selectedCrypto = selectOption.Select();
            if (selectedCrypto == null)
            {
                return;
            }
            byte[] key = Utils.GenerateKey(selectedCrypto.KeyLength);
            FileInfo selected = new SaveFileBuilder()
                .Filter("Ez2On Archive Key file(*.key) | *.key")
                .Select();
            if (selected == null)
            {
                return;
            }
            Utils.WriteFile(key, selected.FullName);
            MessageBox.Show("New Key Saved", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void DecryptBatch()
        {
            List<FileInfo> selectedArchives = new SelectFileBuilder()
                .Filter("Ez2On Data Archive (.tro)|*.tro|Ez2On Music Archive (.dat)|*.dat")
                .SelectMulti();
            if (selectedArchives == null || selectedArchives.Count <= 0)
            {
                return;
            }
            DirectoryInfo selectedDestination = new SelectFolderBuilder()
                .Select();
            if (selectedDestination == null)
            {
                return;
            }
            Ez2OnArchiveIO archiveIO = new Ez2OnArchiveIO();
            archiveIO.ProgressChanged += ArchiveIO_ProgressChanged;
            List<string> errors = new List<string>();
            var task = Task.Run(() =>
            {
                int total = selectedArchives.Count;
                int current = 0;
                float progress = 0;
                byte[] key = null;
                foreach (FileInfo selectedArchive in selectedArchives)
                {
                    Ez2OnArchive archive = archiveIO.Read(selectedArchive.FullName);
                    if (archive.CryptoType == Ez2OnArchive.CRYPTO_TYPE_NONE)
                    {
                        errors.Add($"'{selectedArchive.FullName}': Can not remove encryption. Archive is not encrypted");
                        continue;
                    }
                    Ez2OnArchiveCrypto activeCrypto = GetArchiveCrypto(archive.CryptoType);
                    if (activeCrypto == null)
                    {
                        errors.Add($"'{selectedArchive.FullName}': Can not remove encryption. Archive is encrypted with an unknown encryption");
                        continue;
                    }
                    if (!activeCrypto.CanDecrypt)
                    {
                        errors.Add($"'{selectedArchive.FullName}': Can not remove encryption. '{activeCrypto.Name}' does not support decryption");
                        continue;
                    }
                    if (activeCrypto.CryptoPlugin is ICryptoPlugin)
                    {
                        if (key == null)
                        {
                            FileInfo selectedKey = new SelectFileBuilder().Filter("Ez2On Archive Key file(*.key) | *.key").SelectSingle();
                            if (selectedKey == null)
                            {
                                errors.Add($"'{selectedArchive.FullName}': No key selected for decryption");
                                continue;
                            }
                            key = Utils.ReadFile(selectedKey.FullName);
                        }
                        ICryptoPlugin selectedICryptoPlugin = (ICryptoPlugin)activeCrypto.CryptoPlugin;
                        selectedICryptoPlugin.SetKey(key);
                    }
                    total += archive.Files.Count;
                    foreach (Ez2OnArchiveFile file in archive.Files)
                    {
                        progress = current++ / (float)total * 100;
                        App.UpdateProgress(this, (int)progress, $"'{selectedArchive.FullName}': Decrypting: {file.FullPath}");
                        activeCrypto.Decrypt(file);
                    }
                    archive.CryptoType = Ez2OnArchive.CRYPTO_TYPE_NONE;

                    string destination = Path.Combine(selectedDestination.FullName, selectedArchive.Name);
                    archiveIO.Write(archive, destination);

                    progress = current++ / (float)total * 100;
                    App.UpdateProgress(this, (int)progress, $"'{selectedArchive.FullName}'");
                }
            });
            await task;
            App.ResetProgress(this);
            if (errors.Count > 0)
            {
                string error = "";
                for (int i = 0; i < errors.Count && i < 10; i++)
                {
                    error += errors + Environment.NewLine;
                }
                MessageBox.Show(error, "StepFile", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            MessageBox.Show("Operation Completed", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void EncryptBatch()
        {
            SelectOptionBuilder<Ez2OnArchiveCrypto> selectOption = new SelectOptionBuilder<Ez2OnArchiveCrypto>()
                .SetTitle("Select Crypto")
                .SetText("Choose a crypto to apply");
            bool hasCrypto = false;
            foreach (Ez2OnArchiveCrypto crypto in _cryptos)
            {
                if (crypto.CanEncrypt)
                {
                    selectOption.AddOption(crypto, crypto.Name);
                    hasCrypto = true;
                }
            }
            if (!hasCrypto)
            {
                MessageBox.Show($"Can not encrypt archive. No suitable encryption plugin available", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Ez2OnArchiveCrypto selectedCrypto = selectOption.Select();
            if (selectedCrypto == null)
            {
                return;
            }
            byte[] key = null;
            if (selectedCrypto.CryptoPlugin is ICryptoPlugin)
            {
                ICryptoPlugin selectedICryptoPlugin = (ICryptoPlugin)selectedCrypto.CryptoPlugin;
                FileInfo selected = new SelectFileBuilder()
                    .Filter("Ez2On Archive Key file(*.key) | *.key")
                    .SelectSingle();
                if (selected == null)
                {
                    return;
                }
                key = Utils.ReadFile(selected.FullName);
                selectedICryptoPlugin.SetKey(key);
            }
            List<FileInfo> selectedArchives = new SelectFileBuilder()
                        .Filter("Ez2On Data Archive (.tro)|*.tro|Ez2On Music Archive (.dat)|*.dat")
                        .SelectMulti();
            if (selectedArchives == null || selectedArchives.Count <= 0)
            {
                return;
            }
            DirectoryInfo selectedDestination = new SelectFolderBuilder()
                .Select();
            if (selectedDestination == null)
            {
                return;
            }
            Ez2OnArchiveIO archiveIO = new Ez2OnArchiveIO();
            archiveIO.ProgressChanged += ArchiveIO_ProgressChanged;
            List<string> errors = new List<string>();
            var task = Task.Run(() =>
            {
                int total = selectedArchives.Count;
                int current = 0;
                float progress = 0;
                foreach (FileInfo selectedArchive in selectedArchives)
                {
                    Ez2OnArchive archive = archiveIO.Read(selectedArchive.FullName);
                    Ez2OnArchiveCrypto activeCrypto = null;
                    if (archive.CryptoType != Ez2OnArchive.CRYPTO_TYPE_NONE)
                    {
                        activeCrypto = GetArchiveCrypto(archive.CryptoType);
                        if (activeCrypto == null)
                        {
                            errors.Add($"'{selectedArchive.FullName}': Can not encrypt archive. Archive is encrypted with an unknown encryption");
                            continue;
                        }
                        else
                        {
                            errors.Add($"'{selectedArchive.FullName}': Can not encrypt archive. Archive is encrypted with '{activeCrypto.Name}' encryption");
                            continue;
                        }
                    }
                    total += archive.Files.Count;
                    foreach (Ez2OnArchiveFile file in archive.Files)
                    {
                        progress = current++ / (float)total * 100;
                        App.UpdateProgress(this, (int)progress, $"'{selectedArchive.FullName}': Decrypting: {file.FullPath}");
                        selectedCrypto.Encrypt(file);
                    }
                    archive.CryptoType = selectedCrypto.CryptoType;
                    string destination = Path.Combine(selectedDestination.FullName, selectedArchive.Name);
                    archiveIO.Write(archive, destination);
                    progress = current++ / (float)total * 100;
                    App.UpdateProgress(this, (int)progress, $"'{selectedArchive.FullName}'");
                }
            });
            await task;
            App.ResetProgress(this);
            if (errors.Count > 0)
            {
                string error = "";
                for (int i = 0; i < errors.Count && i < 10; i++)
                {
                    error += errors + Environment.NewLine;
                }
                MessageBox.Show(error, "StepFile", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            MessageBox.Show("Operation Completed", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ListViewItems_Drop(object sender, DragEventArgs e)
        {
            Ez2OnArchiveCrypto activeCrypto = null;
            if (_archive.CryptoType != Ez2OnArchive.CRYPTO_TYPE_NONE)
            {
                activeCrypto = GetArchiveCrypto(_archive.CryptoType);
                if (activeCrypto == null)
                {
                    MessageBox.Show($"Can not add file. Archive is encrypted with an unknown encryption", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (!activeCrypto.CanEncrypt)
                {
                    MessageBox.Show($"Can not add file. Encryption is not supported for '{activeCrypto.Name}'", "StepFile", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] dropped = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string drop in dropped)
                {
                    if (File.Exists(drop))
                    {
                        FileInfo file = App.CreateFileInfo(drop);
                        if (file == null)
                        {
                            continue;
                        }
                        Ez2OnArchiveTabFile tabFile = CreateFile(file, activeCrypto, _currentFolder);
                        if (tabFile == null)
                        {
                            continue;
                        }
                        _archive.Files.Add(tabFile.File);
                        _currentFolder.AddFile(tabFile);
                        _ez2OnArchiveTabControl.Items.Add(tabFile);
                    }
                    else if (Directory.Exists(drop))
                    {
                        DirectoryInfo directory = App.CreateDirectoryInfo(drop);
                        Ez2OnArchiveTabFolder tabFolder = CreateDirectory(directory, activeCrypto, _currentFolder);
                        _currentFolder.Folders.Add(tabFolder);
                        _currentFolder.Folder.Folders.Add(tabFolder.Folder);
                        _ez2OnArchiveTabControl.Items.Add(tabFolder);
                        AddToArchive(tabFolder);
                    }
                    else
                    {
                        _logger.Error($"Can not handle dropped: {drop}");
                    }
                }
            }
        }

        private void ArchiveIO_ProgressChanged(object sender, Ez2OnArchiveIOEventArgs e)
        {
            float progress = e.Current / (float)e.Total * 100;
            App.UpdateProgress(this, (int)progress, e.Message);
        }

        private void ListViewItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Ez2OnArchiveTabFolder selectedFolder = _ez2OnArchiveTabControl.ListViewItems.SelectedItem as Ez2OnArchiveTabFolder;
            if (selectedFolder == null)
            {
                return;
            }
            _ez2OnArchiveTabControl.ClearItems();
            if (_currentFolder.Parent != null)
            {
                _currentFolder.Parent.UpNavigation = false;
            }
            if (selectedFolder.Parent != null)
            {
                selectedFolder.Parent.UpNavigation = true;
                _ez2OnArchiveTabControl.Items.Add(selectedFolder.Parent);
            }
            _ez2OnArchiveTabControl.AddItemRange(selectedFolder.Folders);
            _ez2OnArchiveTabControl.AddItemRange(selectedFolder.Files);
            _currentFolder = selectedFolder;
            _ez2OnArchiveTabControl.FilePath = selectedFolder.FullPath;
        }

        private void ListViewItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ArchiveTabItem selection = _ez2OnArchiveTabControl.ListViewItems.SelectedItem as ArchiveTabItem;
            _currentSelection = selection;
            Ez2OnArchiveTabFile selectedFile = _ez2OnArchiveTabControl.ListViewItems.SelectedItem as Ez2OnArchiveTabFile;
            _currentFile = selectedFile;
            _cmdExtract.RaiseCanExecuteChanged();
            _cmdDelete.RaiseCanExecuteChanged();
        }

        private void LoadArchiveCryptos()
        {
            foreach (IEz2OnArchiveCryptoPlugin archiveCryptoPlugin in PluginRegistry.Instance.GetPlugins<IEz2OnArchiveCryptoPlugin>())
            {
                _cryptos.Add(new Ez2OnArchiveCrypto(archiveCryptoPlugin));
            }
        }

        private Ez2OnArchiveCrypto GetArchiveCrypto(int cryptoType)
        {
            foreach (Ez2OnArchiveCrypto crypto in _cryptos)
            {
                if (crypto.CryptoType == cryptoType)
                {
                    return crypto;
                }
            }
            return null;
        }

        private Ez2OnArchiveTabFolder CreateDirectory(DirectoryInfo directoryInfo, Ez2OnArchiveCrypto activeCrypto, Ez2OnArchiveTabFolder parentTabFolder)
        {
            Ez2OnArchiveFolder folder = new Ez2OnArchiveFolder();
            folder.Name = directoryInfo.Name;
            folder.DirectoryPath = parentTabFolder.FullPath;
            folder.FullPath = parentTabFolder.FullPath + directoryInfo.Name;
            Ez2OnArchiveTabFolder tabFolder = new Ez2OnArchiveTabFolder(folder);
            tabFolder.Parent = parentTabFolder;
            foreach (FileInfo fileInfo in directoryInfo.GetFiles("*", SearchOption.TopDirectoryOnly))
            {
                Ez2OnArchiveTabFile tabFile = CreateFile(fileInfo, activeCrypto, tabFolder);
                if (tabFile == null)
                {
                    continue;
                }
                tabFolder.AddFile(tabFile);
            }
            foreach (DirectoryInfo subFolder in directoryInfo.GetDirectories("*", SearchOption.TopDirectoryOnly))
            {
                Ez2OnArchiveTabFolder subTabFolder = CreateDirectory(subFolder, activeCrypto, tabFolder);
                subTabFolder.Parent = tabFolder;
                tabFolder.Folders.Add(subTabFolder);
                tabFolder.Folder.Folders.Add(subTabFolder.Folder);
            }
            return tabFolder;
        }

        private Ez2OnArchiveTabFile CreateFile(FileInfo fileInfo, Ez2OnArchiveCrypto activeCrypto, Ez2OnArchiveTabFolder tabFolder)
        {
            byte[] file = Utils.ReadFile(fileInfo.FullName);
            Ez2OnArchiveFile archiveFile = new Ez2OnArchiveFile();
            archiveFile.Data = file;
            archiveFile.Name = fileInfo.Name;
            archiveFile.Length = file.Length;
            archiveFile.Extension = fileInfo.Extension;
            archiveFile.DirectoryPath = tabFolder.FullPath;
            archiveFile.FullPath = tabFolder.FullPath + fileInfo.Name;
            if (activeCrypto != null)
            {
                if (activeCrypto.CryptoPlugin is ICryptoPlugin)
                {
                    ICryptoPlugin selectedICryptoPlugin = (ICryptoPlugin)activeCrypto.CryptoPlugin;
                    if (_crypoKey == null)
                    {
                        FileInfo selectedKey = new SelectFileBuilder()
                            .Filter("Ez2On Archive Key file(*.key) | *.key")
                            .SelectSingle();
                        if (selectedKey == null)
                        {
                            return null;
                        }
                        _crypoKey = Utils.ReadFile(selectedKey.FullName);
                    }
                    selectedICryptoPlugin.SetKey(_crypoKey);
                }
                activeCrypto.Encrypt(archiveFile);
            }
            Ez2OnArchiveTabFile tabFile = new Ez2OnArchiveTabFile(archiveFile);
            return tabFile;
        }

        private void AddToArchive(Ez2OnArchiveTabFolder parentTabFolder)
        {
            foreach (Ez2OnArchiveTabFile tabFile in parentTabFolder.Files)
            {
                _archive.Files.Add(tabFile.File);
            }
            foreach (Ez2OnArchiveTabFolder tabFolder in parentTabFolder.Folders)
            {
                AddToArchive(tabFolder);
            }
        }
    }
}
