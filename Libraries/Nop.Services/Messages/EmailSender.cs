﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using Amazon.Runtime;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using MimeKit;
using Nop.Core.Domain.Messages;
using Nop.Services.Configuration;
using Nop.Services.Media;

namespace Nop.Services.Messages
{
    /// <summary>
    /// Email sender
    /// </summary>
    public partial class EmailSender : IEmailSender
    {
        private readonly IDownloadService _downloadService;
        private readonly ISettingService _settingService;

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="downloadService">Download service</param>
        public EmailSender(IDownloadService downloadService, ISettingService settingService)
        {
            this._downloadService = downloadService;
            this._settingService = settingService;
        }

        /// <summary>
        /// Sends an email
        /// </summary>
        /// <param name="emailAccount">Email account to use</param>
        /// <param name="subject">Subject</param>
        /// <param name="body">Body</param>
        /// <param name="fromAddress">From address</param>
        /// <param name="fromName">From display name</param>
        /// <param name="toAddress">To address</param>
        /// <param name="toName">To display name</param>
        /// <param name="replyTo">ReplyTo address</param>
        /// <param name="replyToName">ReplyTo display name</param>
        /// <param name="bcc">BCC addresses list</param>
        /// <param name="cc">CC addresses list</param>
        /// <param name="attachmentFilePath">Attachment file path</param>
        /// <param name="attachmentFileName">Attachment file name. If specified, then this file name will be sent to a recipient. Otherwise, "AttachmentFilePath" name will be used.</param>
        /// <param name="attachedDownloadId">Attachment download ID (another attachedment)</param>
        /// <param name="headers">Headers</param>
        public virtual void SendEmail(EmailAccount emailAccount, string subject, string body,
            string fromAddress, string fromName, string toAddress, string toName,
             string replyTo = null, string replyToName = null,
            IEnumerable<string> bcc = null, IEnumerable<string> cc = null,
            string attachmentFilePath = null, string attachmentFileName = null,
            int attachedDownloadId = 0, IDictionary<string, string> headers = null)
        {



            // Create and build a new MailMessage object
            MailMessage message = new MailMessage();
            message.IsBodyHtml = true;
            message.From = new MailAddress(fromAddress, fromName);
            message.To.Add(new MailAddress(toAddress, toName));
            message.Subject = subject;
            message.Body = body;

            //CC
            if (cc != null)
            {
                foreach (var address in cc.Where(ccValue => !string.IsNullOrWhiteSpace(ccValue)))
                {
                    message.CC.Add(address.Trim());
                }
            }

            if (!string.IsNullOrEmpty(replyTo))
            {
                message.ReplyToList.Add(replyTo); //(new MailAddress(replyTo, replyToName));
            }

            if (bcc != null)
            {
                foreach (var address in bcc.Where(bccValue => !string.IsNullOrWhiteSpace(bccValue)))
                {
                    message.Bcc.Add(address.Trim());
                }
            }


            if (!string.IsNullOrEmpty(attachmentFilePath) &&
              File.Exists(attachmentFilePath))
                message.Attachments.Add(new Attachment(attachmentFilePath));

            // Comment or delete the next line if you are not using a configuration set
            // message.Headers.Add("X-SES-CONFIGURATION-SET", "ConfigSet");

            using (var client1 = new System.Net.Mail.SmtpClient(emailAccount.Host, emailAccount.Port))
            {
                // Pass SMTP credentials
                client1.Credentials =
                    new NetworkCredential(emailAccount.Username, emailAccount.Password);

                // Enable SSL encryption
                client1.EnableSsl = emailAccount.EnableSsl;

                client1.Send(message);

                // Try to send the message. Show status in console.
                //try
                //{
                //    Console.WriteLine("Attempting to send email...");
                //    client1.Send(message);
                //    Console.WriteLine("Email sent!");
                //}
                //catch (Exception ex)
                //{
                //    Console.WriteLine("The email was not sent.");
                //    Console.WriteLine("Error message: " + ex.Message);
                //}
            }

        }
            // Amazon Simple Email Service Client




            //String awsAccessKey = "AKIAISCDDROTDTJ66TFQ";    // Replace with your AWS access key.
            //String awsSecretKey = "yxoYAm5qhVqVlUk0FffeVBHI7OuHO8N4DmRvQgnv";    // Replace with your AWS secret key.


            //awsAccessKey=  _settingService.GetSetting("awsAccessKey").Value.Trim();
            //awsSecretKey = _settingService.GetSetting("awsSecretKey").Value.Trim();

            ////String source = "Arc Services Co. <websupport@arcservicesco.com>";

            //var credentals = new BasicAWSCredentials(emailAccount.Username, emailAccount.Password);

            //Content subjectAws = new Content(subject);

            //using (var client = new AmazonSimpleEmailServiceClient(credentals, Amazon.RegionEndpoint.USEast1))
            //using (var messageStream = new MemoryStream())
            //{
            //    var messageAws = new MimeMessage();
            //    var builder = new BodyBuilder() { TextBody = body, HtmlBody= body };

            //    messageAws.From.Add(new MailboxAddress(fromName,fromAddress));
            //    messageAws.To.Add(new MailboxAddress(toName, toAddress));

            //    messageAws.Subject = subject;


            //    if (!string.IsNullOrEmpty(replyTo))
            //    {
            //        messageAws.ReplyTo.Add(new MailboxAddress(replyToName, replyTo)); //(new MailAddress(replyTo, replyToName));
            //    }

            //    //BCC
            //    if (bcc != null)
            //    {
            //        foreach (var address in bcc.Where(bccValue => !string.IsNullOrWhiteSpace(bccValue)))
            //        {
            //            messageAws.Bcc.Add(new MailboxAddress(address.Trim()));
            //        }
            //    }

            //    //CC
            //    if (cc != null)
            //    {
            //        foreach (var address in cc.Where(ccValue => !string.IsNullOrWhiteSpace(ccValue)))
            //        {
            //            messageAws.Cc.Add(new MailboxAddress(address.Trim()));
            //        }
            //    }

            //    //create the file attachment for this e-mail message
            //    if (!string.IsNullOrEmpty(attachmentFilePath) &&
            //        File.Exists(attachmentFilePath))
            //    {
            //        using (FileStream stream = File.Open(attachmentFilePath, FileMode.Open)) builder.Attachments.Add(attachmentFilePath, stream);
            //    }
            //    //another attachment?
            //    if (attachedDownloadId > 0)
            //    {
            //        var download = _downloadService.GetDownloadById(attachedDownloadId);
            //        if (download != null)
            //        {
            //            //we do not support URLs as attachments
            //            if (!download.UseDownloadUrl)
            //            {
            //                var fileName = !string.IsNullOrWhiteSpace(download.Filename) ? download.Filename : download.Id.ToString();
            //                fileName += download.Extension;
            //                using (FileStream stream = File.Open(fileName, FileMode.Open)) builder.Attachments.Add(fileName, stream);
            //            }
            //        }
            //    }

            //    messageAws.Body = builder.ToMessageBody();
            //    messageAws.WriteTo(messageStream);

            //    var request = new SendRawEmailRequest()
            //    {
            //        RawMessage = new RawMessage() { Data = messageStream }
            //    };

            //    try
            //    {
            //        client.SendRawEmail(request);
            //    }
            //    catch
            //    { }


         





            //var message = new MailMessage
            //{
            //    //from, to, reply to
            //    From = new MailAddress(fromAddress, fromName)
            //};
            //message.To.Add(new MailAddress(toAddress, toName));
            //if (!string.IsNullOrEmpty(replyTo))
            //{
            //    message.ReplyToList.Add(new MailAddress(replyTo, replyToName));
            //}

            ////BCC
            //if (bcc != null)
            //{
            //    foreach (var address in bcc.Where(bccValue => !string.IsNullOrWhiteSpace(bccValue)))
            //    {
            //        message.Bcc.Add(address.Trim());
            //    }
            //}

            ////CC
            //if (cc != null)
            //{
            //    foreach (var address in cc.Where(ccValue => !string.IsNullOrWhiteSpace(ccValue)))
            //    {
            //        message.CC.Add(address.Trim());
            //    }
            //}

            ////content
            //message.Subject = subject;
            //message.Body = body;
            //message.IsBodyHtml = true;

            ////headers
            //if (headers != null)
            //    foreach (var header in headers)
            //    {
            //        message.Headers.Add(header.Key, header.Value);
            //    }

            ////create the file attachment for this e-mail message
            //if (!string.IsNullOrEmpty(attachmentFilePath) &&
            //    File.Exists(attachmentFilePath))
            //{
            //    var attachment = new Attachment(attachmentFilePath);
            //    attachment.ContentDisposition.CreationDate = File.GetCreationTime(attachmentFilePath);
            //    attachment.ContentDisposition.ModificationDate = File.GetLastWriteTime(attachmentFilePath);
            //    attachment.ContentDisposition.ReadDate = File.GetLastAccessTime(attachmentFilePath);
            //    if (!string.IsNullOrEmpty(attachmentFileName))
            //    {
            //        attachment.Name = attachmentFileName;
            //    }
            //    message.Attachments.Add(attachment);
            //}
            ////another attachment?
            //if (attachedDownloadId > 0)
            //{
            //    var download = _downloadService.GetDownloadById(attachedDownloadId);
            //    if (download != null)
            //    {
            //        //we do not support URLs as attachments
            //        if (!download.UseDownloadUrl)
            //        {
            //            var fileName = !string.IsNullOrWhiteSpace(download.Filename) ? download.Filename : download.Id.ToString();
            //            fileName += download.Extension;


            //            var ms = new MemoryStream(download.DownloadBinary);                        
            //            var attachment = new Attachment(ms, fileName);
            //            //string contentType = !string.IsNullOrWhiteSpace(download.ContentType) ? download.ContentType : "application/octet-stream";
            //            //var attachment = new Attachment(ms, fileName, contentType);
            //            attachment.ContentDisposition.CreationDate = DateTime.UtcNow;
            //            attachment.ContentDisposition.ModificationDate = DateTime.UtcNow;
            //            attachment.ContentDisposition.ReadDate = DateTime.UtcNow;
            //            message.Attachments.Add(attachment);                        
            //        }
            //    }
            //}

            ////send email
            //using (var smtpClient = new SmtpClient())
            //{
            //    smtpClient.UseDefaultCredentials = emailAccount.UseDefaultCredentials;
            //    smtpClient.Host = emailAccount.Host;
            //    smtpClient.Port = emailAccount.Port;
            //    smtpClient.EnableSsl = emailAccount.EnableSsl;
            //    smtpClient.Credentials = emailAccount.UseDefaultCredentials ? 
            //        CredentialCache.DefaultNetworkCredentials :
            //        new NetworkCredential(emailAccount.Username, emailAccount.Password);
            //    smtpClient.Send(message);
            //}


     
    }
}
