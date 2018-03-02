﻿// Copyright (c) Source Tree Solutions, LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Author:					Joe Audette
// Created:					2018-03-01
// Last Modified:			2018-03-02
// 

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

//https://api.elasticemail.com/public/help#Email_Send
//https://elasticemail.com/resources/api/attachments-upload/

namespace cloudscribe.Messaging.Email.ElasticEmail
{
    public class ElasticEmailSender : IEmailSender
    {
        public ElasticEmailSender(
            IElasticEmailOptionsProvider optionsProvider,
            ILogger<ElasticEmailSender> logger
            )
        {
            _optionsProvider = optionsProvider;
            _log = logger;
        }

        private IElasticEmailOptionsProvider _optionsProvider;
        private ILogger _log;

        public string Name { get; } = "ElasticEmailSender";

        private ElasticEmailOptions options = null;
        public async Task<bool> IsConfigured(string configLookupKey = null)
        {
            if(options == null)
            {
                options = await _optionsProvider.GetElasticEmailOptions(configLookupKey);
            }
            
            if (options == null || string.IsNullOrWhiteSpace(options.ApiKey))
            {
                return false;
            }
            return true;

        }

        public async Task SendEmailAsync(
            string toEmailCsv,
            string fromEmail,
            string subject,
            string plainTextMessage,
            string htmlMessage,
            string replyToEmail = null,
            Importance importance = Importance.Normal,
            bool isTransactional = true,
            string fromName = null,
            string replyToName = null,
            string toAliasCsv = null,
            string ccEmailCsv = null,
            string ccAliasCsv = null,
            string bccEmailCsv = null,
            string bccAliasCsv = null,
            string[] attachmentFilePaths = null,
            string charsetBodyHtml = null,
            string charsetBodyText = null,
            string configLookupKey = null
            )
        {
            var isConfigured = await IsConfigured(configLookupKey);
            
            if (!isConfigured)
            {
                _log.LogError($"failed to send email with subject {subject} because elasticemail api key is empty or not configured");

                return;
            }

            if (string.IsNullOrWhiteSpace(toEmailCsv))
            {
                throw new ArgumentException("no to addresses provided");
            }

            if (string.IsNullOrWhiteSpace(fromEmail) && string.IsNullOrWhiteSpace(options.DefaultEmailFromAddress))
            {
                throw new ArgumentException("no from address provided");
            }

            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new ArgumentException("no subject provided");
            }

            var hasPlainText = !string.IsNullOrWhiteSpace(plainTextMessage);
            var hasHtml = !string.IsNullOrWhiteSpace(htmlMessage);
            if (!hasPlainText && !hasHtml)
            {
                throw new ArgumentException("no message provided");
            }
            
#pragma warning disable IDE0028 // Simplify collection initialization
            var keyValues = new List<KeyValuePair<string, string>>();
#pragma warning restore IDE0028 // Simplify collection initialization

            if(!string.IsNullOrWhiteSpace(fromEmail))
            {
                keyValues.Add(new KeyValuePair<string, string>("from", fromEmail));
                if (!string.IsNullOrWhiteSpace(fromName))
                {
                    keyValues.Add(new KeyValuePair<string, string>("fromName", fromName));
                }
            }
            else
            {
                keyValues.Add(new KeyValuePair<string, string>("from", options.DefaultEmailFromAddress));
                if (!string.IsNullOrWhiteSpace(options.DefaultEmailFromAlias))
                {
                    keyValues.Add(new KeyValuePair<string, string>("fromName", options.DefaultEmailFromAlias));
                }
            }
            
            if (!string.IsNullOrWhiteSpace(replyToEmail))
            {
                keyValues.Add(new KeyValuePair<string, string>("replyTo", replyToEmail));
            }
            if (!string.IsNullOrWhiteSpace(replyToName))
            {
                keyValues.Add(new KeyValuePair<string, string>("replyToName", replyToName));
            }


            keyValues.Add(new KeyValuePair<string, string>("subject", subject));
            if(!string.IsNullOrWhiteSpace(htmlMessage))
            {
                keyValues.Add(new KeyValuePair<string, string>("bodyHtml", htmlMessage));
            }

            if (!string.IsNullOrWhiteSpace(plainTextMessage))
            {
                keyValues.Add(new KeyValuePair<string, string>("bodyText", plainTextMessage));
            }

            if(!string.IsNullOrWhiteSpace(charsetBodyHtml))
            {
                keyValues.Add(new KeyValuePair<string, string>("charsetBodyHtml", charsetBodyHtml));
            }
            if (!string.IsNullOrWhiteSpace(charsetBodyText))
            {
                keyValues.Add(new KeyValuePair<string, string>("charsetBodyText", charsetBodyText));
            }

            keyValues.Add(new KeyValuePair<string, string>("isTransactional", isTransactional.ToString().ToLower()));
                
            keyValues.Add(new KeyValuePair<string, string>("msgTo", toEmailCsv));
            if(!string.IsNullOrWhiteSpace(ccEmailCsv))
            {
                keyValues.Add(new KeyValuePair<string, string>("msgCC", ccEmailCsv));
            }
            if (!string.IsNullOrWhiteSpace(bccEmailCsv))
            {
                keyValues.Add(new KeyValuePair<string, string>("msgBcc", bccEmailCsv));
            }


            if(attachmentFilePaths == null || attachmentFilePaths.Length == 0)
            {
                await SendWithoutAttachments(keyValues, options, subject);
            }
            else
            {
                var filesStream = new List<Stream>();
                var fileNames = new List<string>();
                foreach (var filePath in attachmentFilePaths)
                {
                    try
                    {
                        var file = File.OpenRead(filePath);
                        filesStream.Add(file);
                        fileNames.Add(Path.GetFileName(filePath));
                    }
                    catch (Exception ex)
                    {
                        _log.LogError($"failed to add attachment with path {filePath}, error was {ex.Message} : {ex.StackTrace}");
                    }
                }

                await SendWithAttachments(keyValues, options, subject, filesStream.ToArray(), fileNames.ToArray());

            }
               
            
        }

        private async Task SendWithoutAttachments(List<KeyValuePair<string, string>> keyValues, ElasticEmailOptions options, string subject)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                  new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes(options.ApiKey)));

                var content = new FormUrlEncodedContent(keyValues);

                try
                {
                    var response = await client.PostAsync(options.EndpointUrl, content).ConfigureAwait(false);
                    var result = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        _log.LogError($"failed to send email with subject {subject} error was {response.StatusCode} : {result}");
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError($"failed to send email with subject {subject} error was {ex.Message} : {ex.StackTrace}");
                }

            }

                
        }

        public async Task SendWithAttachments(List<KeyValuePair<string, string>> keyValues, ElasticEmailOptions options, string subject, Stream[] paramFileStream = null, string[] filenames = null)
        {
            using (var client = new HttpClient())
            using (var formData = new MultipartFormDataContent())
            {
                client.DefaultRequestHeaders.Authorization =
                  new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes(options.ApiKey)));

                foreach (var item in keyValues)
                {
                    HttpContent stringContent = new StringContent(item.Value);
                    formData.Add(stringContent, item.Key);
                }

                for (int i = 0; i < paramFileStream.Length; i++)
                {
                    HttpContent fileStreamContent = new StreamContent(paramFileStream[i]);
                    formData.Add(fileStreamContent, "file" + i, filenames[i]);
                }

                try
                {
                    var response = await client.PostAsync(options.EndpointUrl, formData);
                    var result = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        _log.LogError($"failed to send email with subject {subject} error was {response.StatusCode} : {result}");
                    }

                    
                }
                catch(Exception ex)
                {
                    _log.LogError($"failed to send email with subject {subject} error was {ex.Message} : {ex.StackTrace}");
                }

                
            }
        }


    }
}