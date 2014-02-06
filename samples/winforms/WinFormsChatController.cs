using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Twitch;
using ErrorCode = Twitch.ErrorCode;

namespace Twitch.Chat
{
    public class WinFormsChatController : ChatController
    {
        #region Memeber Variables

        protected string m_ClientId = "";
        protected string m_ClientSecret = "";
        protected EmoticonMode m_EmoticonMode = EmoticonMode.None;
        protected int m_MessageFlushInterval = 500;
        protected int m_UserChangeEventInterval = 2000;

        #endregion

        #region Properties

        public override string ClientId
        {
            get { return m_ClientId; }
            set { m_ClientId = value; }
        }

        public override string ClientSecret
        {
            get { return m_ClientSecret; }
            set { m_ClientSecret = value; }
        }

        public override EmoticonMode EmoticonParsingMode
        {
            get { return m_EmoticonMode; }
            set { m_EmoticonMode = value; }
        }

        public override int MessageFlushInterval
        {
            get { return m_MessageFlushInterval; }
            set
            {
                value = Math.Min(MAX_INTERVAL_MS, Math.Max(value, MIN_INTERVAL_MS));

                m_MessageFlushInterval = value;
                base.MessageFlushInterval = value;
            }
        }

        public override int UserChangeEventInterval
        {
            get { return m_UserChangeEventInterval; }
            set
            {
                value = Math.Min(MAX_INTERVAL_MS, Math.Max(value, MIN_INTERVAL_MS));

                m_UserChangeEventInterval = value;
                base.UserChangeEventInterval = value;
            }
        }
        
        #endregion

        public WinFormsChatController()
        {
            m_Core = Core.Instance;

            if (m_Core == null)
            {
                m_Core = new Core(new StandardCoreAPI());
            }

            m_Chat = new Twitch.Chat.Chat(new Twitch.Chat.StandardChatAPI());
        }

        #region Error Handling

        protected override void CheckError(ErrorCode err)
        {
            if (err != ErrorCode.TTV_EC_SUCCESS)
            {
                System.Windows.Forms.MessageBox.Show(err.ToString());
            }
        }

        protected override void ReportError(string err)
        {
            System.Windows.Forms.MessageBox.Show(err, "Error");
        }

        protected override void ReportWarning(string err)
        {
            System.Windows.Forms.MessageBox.Show(err, "Warning");
        }

        #endregion
    }
}
