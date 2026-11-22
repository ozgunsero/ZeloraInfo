// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using ZeloraInfo.Core.Localization;

namespace ZeloraInfo.App.Services;

/// <summary>Clipboard, dialogs, toasts and shell helpers used by view models (keeps them free of WPF types).</summary>
public sealed class UiService
{
    public event EventHandler<string>? ToastRequested;

    public void Toast(string message) => ToastRequested?.Invoke(this, message);

    public bool CopyToClipboard(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                Toast(LanguageCatalog.Get("Common.Copied"));
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(60); // clipboard temporarily locked by another process
            }
        }
        ShowError(LanguageCatalog.Format("Common.ExportFailed", "clipboard busy"));
        return false;
    }

    /// <summary>Shows a Save dialog and writes the text (UTF-8). Returns the path or null.</summary>
    public string? SaveText(string defaultFileName, string filter, string content)
    {
        var dialog = new SaveFileDialog
        {
            FileName = defaultFileName,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return null;
        try
        {
            File.WriteAllText(dialog.FileName, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Toast(LanguageCatalog.Format("Common.Saved", Path.GetFileName(dialog.FileName)));
            return dialog.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(LanguageCatalog.Format("Common.ExportFailed", ex.Message));
            return null;
        }
    }

    public string? PickFolder(string title, string? initial)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = initial ?? "" };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    public bool Confirm(string title, string message)
        => MessageBox.Show(Application.Current.MainWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowError(string message)
        => MessageBox.Show(Application.Current.MainWindow!, message, LanguageCatalog.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message)
        => MessageBox.Show(Application.Current.MainWindow!, message, LanguageCatalog.Get("App.Title"), MessageBoxButton.OK, MessageBoxImage.Information);

    public void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    public void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
