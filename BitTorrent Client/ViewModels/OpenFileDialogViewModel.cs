﻿using System;
using Microsoft.Win32;

namespace BitTorrent_Client.ViewModels
{
    /// <summary>
    /// This class is responsible for opening an open file dialog for the main 
    /// view model.
    /// </summary>
    public class OpenFileDialogViewModel
    {
        /// <summary>
        /// Opens a file dialog to select torrent.
        /// </summary>
        /// <returns>Returns an open file dialog</returns>
        /// <remarks>
        /// OpenFileDialog()
        /// 
        /// SYNOPSIS
        /// 
        ///     OpenFileDialog OpenFileDialog();
        ///     
        /// DESCRIPTION
        /// 
        ///     This function will create an open dialog and have the user 
        ///     select a torrent to download. The OpenFileDialog is returned.
        ///     
        /// </remarks>
        public OpenFileDialog OpenFileDialog()
        {
            var openFileDialog = new OpenFileDialog();

            openFileDialog.DefaultExt = ".torrent";
            openFileDialog.Filter = "Torrent file (.torrent)|*.torrent";

            Nullable<bool> result = openFileDialog.ShowDialog();

            if (result == true)
            {
                return openFileDialog;
            }
            return null;
        }
    }
}