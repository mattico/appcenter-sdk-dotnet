﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using Microsoft.AspNetCore.StaticFiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace Contoso.WinForms.Demo.DotNetCore
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);

            AppCenter.LogLevel = LogLevel.Verbose;
            Crashes.GetErrorAttachments = GetErrorAttachmentsHandler;

            var storageMaxSize = Settings.Default.StorageMaxSize;
            if (storageMaxSize > 0)
            {
                AppCenter.SetMaxStorageSizeAsync(storageMaxSize);
            }
            AppCenter.Start("734be4f7-3607-489b-ae81-284d2eb908f8", typeof(Analytics), typeof(Crashes));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static IEnumerable<ErrorAttachmentLog> GetErrorAttachmentsHandler(ErrorReport report)
        {
            return GetErrorAttachments();
        }

        public static IEnumerable<ErrorAttachmentLog> GetErrorAttachments()
        {
            List<ErrorAttachmentLog> attachments = new List<ErrorAttachmentLog>();

            // Text attachment
            if (!string.IsNullOrEmpty(Settings.Default.TextErrorAttachments))
            {
                attachments.Add(
                    ErrorAttachmentLog.AttachmentWithText(Settings.Default.TextErrorAttachments, "text.txt"));
            }

            // Binary attachment
            if (!string.IsNullOrEmpty(Settings.Default.FileErrorAttachments))
            {
                if (File.Exists(Settings.Default.FileErrorAttachments))
                {
                    var fileName = new FileInfo(Settings.Default.FileErrorAttachments).Name;
                    var provider = new FileExtensionContentTypeProvider();
                    if (!provider.TryGetContentType(fileName, out var contentType))
                    {
                        contentType = "application/octet-stream";
                    }
                    var fileContent = File.ReadAllBytes(Settings.Default.FileErrorAttachments);
                    attachments.Add(ErrorAttachmentLog.AttachmentWithBinary(fileContent, fileName, contentType));
                }
                else
                {
                    Settings.Default.FileErrorAttachments = null;
                }
            }

            return attachments;
        }
    }
}
