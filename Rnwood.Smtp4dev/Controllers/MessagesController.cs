﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Rnwood.Smtp4dev.ApiModel;
using System.Linq.Dynamic.Core;
using Microsoft.EntityFrameworkCore;
using Message = Rnwood.Smtp4dev.DbModel.Message;
using Rnwood.Smtp4dev.Server;
using MimeKit;
using Rnwood.Smtp4dev.Data;
using Rnwood.Smtp4dev.DbModel;

namespace Rnwood.Smtp4dev.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [UseEtagFilterAttribute]
    public class MessagesController : Controller
    {
        public MessagesController(IMessagesRepository messagesRepository, ISmtp4devServer server)
        {
            this.messagesRepository = messagesRepository;
            this.server = server;
        }

        private readonly IMessagesRepository messagesRepository;
        private readonly ISmtp4devServer server;

        [HttpGet]
        public ApiModel.PagedResult<MessageSummary> GetSummaries(string sortColumn = "receivedDate", bool sortIsDescending = true, int page = 1, int pageSize=5)
        {
            return messagesRepository.GetMessages(false).Include(m => m.Relays)
                .OrderBy(sortColumn + (sortIsDescending ? " DESC" : ""))
                .Select(m => new MessageSummary(m))
                .GetPaged(page, pageSize);
        }

        private Message GetDbMessage(Guid id)
        {
            return messagesRepository.GetMessages(false).SingleOrDefault(m => m.Id == id) ??
                   throw new FileNotFoundException($"Message with id {id} was not found.");
        }

        [HttpGet("{id}")]
        public ApiModel.Message GetMessage(Guid id)
        {
            return new ApiModel.Message(GetDbMessage(id));
        }

        [HttpPost("{id}")]
        public Task MarkMessageRead(Guid id)
        {
            return messagesRepository.MarkMessageRead(id);
        }

        [HttpGet("{id}/download")]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 31556926)]
        public FileStreamResult DownloadMessage(Guid id)
        {
            Message result = GetDbMessage(id);
            return new FileStreamResult(new MemoryStream(result.Data), "message/rfc822") { FileDownloadName = $"{id}.eml" };
        }

        [HttpPost("{id}/relay")]
        public IActionResult RelayMessage(Guid id, [FromBody] MessageRelayOptions options)
        {
            var message = GetDbMessage(id);
            var relayResult = server.TryRelayMessage(message,
                options?.OverrideRecipientAddresses?.Length > 0
                    ? options?.OverrideRecipientAddresses.Select(a => MailboxAddress.Parse(a)).ToArray()
                    : null);

            if (relayResult.Exceptions.Any())
            {
                var relayErrorSummary = string.Join(". ", relayResult.Exceptions.Select(e => e.Key.Address + ": " + e.Value.Message));
                return Problem("Failed to relay to recipients: " + relayErrorSummary);
            }
            if (relayResult.WasRelayed)
            {
                foreach (var relay in relayResult.RelayRecipients)
                {
                    message.AddRelay(new MessageRelay { SendDate = relay.RelayDate, To = relay.Email });
                }
                messagesRepository.DbContext.SaveChanges();
            }
            return Ok();
        }

        [HttpGet("{id}/part/{partid}/content")]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 31556926)]
        public FileStreamResult GetPartContent(Guid id, string partid)
        {
            return ApiModel.Message.GetPartContent(GetMessage(id), partid);
        }

        [HttpGet("{id}/part/{partid}/source")]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 31556926)]
        public string GetPartSource(Guid id, string partid)
        {
            return ApiModel.Message.GetPartContentAsText(GetMessage(id), partid);
        }

        [HttpGet("{id}/part/{partid}/raw")]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 31556926)]
        public string GetPartSourceRaw(Guid id, string partid)
        {
            return ApiModel.Message.GetPartSource(GetMessage(id), partid);
        }

        [HttpGet("{id}/raw")]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 31556926)]
        public string GetMessageSourceRaw(Guid id)
        {
            ApiModel.Message message = GetMessage(id);
            return System.Text.Encoding.UTF8.GetString(message.Data);
        }

        [HttpGet("{id}/source")]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 31556926)]
        public string GetMessageSource(Guid id)
        {
            ApiModel.Message message = GetMessage(id);
            return message.MimeMessage.ToString();
        }

        [HttpGet("{id}/html")]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 31556926)]
        public string GetMessageHtml(Guid id)
        {
            ApiModel.Message message = GetMessage(id);

            string html = message.MimeMessage?.HtmlBody;

            if (html == null)
            {
                html = "<pre>" + HtmlAgilityPack.HtmlDocument.HtmlEncode(message.MimeMessage?.TextBody ?? "") + "</pre>";
            }


            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);


            HtmlNodeCollection imageElements = doc.DocumentNode.SelectNodes("//img[starts-with(@src, 'cid:')]");

            if (imageElements != null)
            {
                foreach (HtmlNode imageElement in imageElements)
                {
                    string cid = imageElement.Attributes["src"].Value.Replace("cid:", "", StringComparison.OrdinalIgnoreCase);

                    var part = message.Parts.Flatten(p => p.ChildParts).SingleOrDefault(p => p.ContentId == cid);

                    imageElement.Attributes["src"].Value = $"api/Messages/{id.ToString()}/part/{part?.Id ?? "notfound"}/content";
                }
            }

            return doc.DocumentNode.OuterHtml;
        }

        [HttpDelete("{id}")]
        public async Task Delete(Guid id)
        {
            await messagesRepository.DeleteMessage(id);
        }

        [HttpDelete("*")]
        public async Task DeleteAll()
        {
            await messagesRepository.DeleteAllMessages();
        }
    }
}