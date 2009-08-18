using System;
using System.IO;
using System.Text;
using Rnwood.SmtpServer.Extensions;
using Rnwood.SmtpServer.Verbs;

namespace Rnwood.SmtpServer
{
    public interface IConnectionProcessor
    {
        Server Server { get; }
        ExtensionProcessor[] ExtensionProcessors { get; }
        VerbMap VerbMap { get; }
        MailVerb MailVerb { get; }
        Session Session { get; }
        Message CurrentMessage { get; }
        void SwitchReaderEncoding(Encoding encoding);
        void SwitchReaderEncodingToDefault();
        void CloseConnection();
        void ApplyStreamFilter(Func<Stream, Stream> filter);
        void WriteLine(string text, params object[] arg);
        void WriteResponse(SmtpResponse response);
        string ReadLine();
        Message NewMessage();
        void CommitMessage();
        void AbortMessage();
    }
}