using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace IngestTesterGui
{
    public partial class IngestTesterForm: Form
    {
        protected Twitch.Broadcast.BroadcastController mBroadcastController = new Twitch.Broadcast.WinFormsBroadcastController();
        protected Twitch.Broadcast.IngestTester mIngestTester = null;
        protected String mTwitchUsername = "";
        protected String mTwitchPassword = "";
        protected bool mBeginIngestTest = false;

        static protected String mClientId = "swug4pkjx9y9emgvbsr5bq5aneytm4s";
        static protected String mClientSecret = "qs5ihbacb5mt9iw5omjdx3p6nohasbh";

        public IngestTesterForm()
        {
            InitializeComponent();

            mBroadcastController.AuthTokenRequestComplete += this.HandleAuthTokenRequestComplete;
            mBroadcastController.BroadcastStateChanged += this.HandleBroadcastStateChanged;
            mBroadcastController.LoginAttemptComplete += this.HandleLoginAttemptComplete;
            mBroadcastController.IngestListReceived += this.HandleIngestListReceived;

            mBroadcastController.ClientId = mClientId;
            mBroadcastController.ClientSecret = mClientSecret;
            mBroadcastController.Initialize();
        }

        private void IngestTesterForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            mBroadcastController.ForceSyncShutdown();
        }

        protected void HandleAuthTokenRequestComplete(Twitch.ErrorCode result, Twitch.AuthToken authToken)
        {
            if (Twitch.Error.Failed(result))
            {
                mTwitchUsername = null;
                mTwitchPassword = null;
                MessageBox.Show("Login failed, please check your credentials and try again.");
            }
        }

        protected void HandleLoginAttemptComplete(Twitch.ErrorCode result)
        {
            if (Twitch.Error.Failed(result))
            {
                mTwitchUsername = null;
                mTwitchPassword = null;
                MessageBox.Show("Login failed, please check your credentials and try again.");
            }
        }

        protected void HandleBroadcastStateChanged(Twitch.Broadcast.BroadcastController.BroadcastState state)
        {
            String stateString = state.ToString();
            if (state == Twitch.Broadcast.BroadcastController.BroadcastState.ReadyToBroadcast)
            {
                // If reached the ReadyToBroadcast state from "Start Ingest Test" button press
                if (mBeginIngestTest)
                {
                    mBeginIngestTest = false;
                    if (BeginIngestTest())
                    {
                        stateString = "Ingest Testing";
                    }
                    else
                    {
                        MessageBox.Show("Failed to start ingest tester, please try again");
                        stateString = "Error";
                    }
                }
            }
            mStatusLabel.Text = "Status - " + stateString;
        }

        protected class IngestListEntry
        {
            protected Twitch.Broadcast.IngestServer mServer = null;
            public bool Default { get; set; }
            public bool Selected { get; set; }

            public Twitch.Broadcast.IngestServer Server
            {
                get { return mServer; }
            }

            public IngestListEntry(Twitch.Broadcast.IngestServer server)
            {
                mServer = server;
            }

            public override string ToString()
            {
                string str = mServer.ServerName;
                if (this.Default) str += " (Default)";
                if (this.Selected) str = "* " + str;
                str += " - " + mServer.BitrateKbps + " kbps";

                return str;
            }
        }

        protected void HandleIngestListReceived(Twitch.Broadcast.IngestList list)
        {
            mIngestListListbox.Items.Clear();

            for (int i = 0; i < list.Servers.Length; ++i)
            {
                IngestListEntry item = new IngestListEntry(list.Servers[i]);
                item.Default = item.Server == list.DefaultServer;
                item.Selected = item.Server == mBroadcastController.IngestServer;
                mIngestListListbox.Items.Add(item);
            }
        }

        protected void RefreshListbox(ListBox listbox)
        {
            typeof(ListBox).InvokeMember("RefreshItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.InvokeMethod, null, listbox, new object[] { });
        }

        private void mStreamTasksTimer_Tick(object sender, EventArgs e)
        {
            GC.Collect();

            mBroadcastController.Update();

            if (mIngestTester != null)
            {
                if (mIngestTester.State == Twitch.Broadcast.IngestTester.TestState.TestingServer)
                {
                    mIngestTestStatusText.Text = "[" + (int)(mIngestTester.TotalProgress * 100) + "%] " + mIngestTester.State.ToString() + ": " + mIngestTester.CurrentServer.ServerName + "... " + mIngestTester.CurrentServer.BitrateKbps + " kbps [" + (int)(mIngestTester.ServerProgress * 100) + "%]";
                    RefreshListbox(mIngestListListbox);
                }
            }
        }

        private void mStartIngestTestButton_Click(object sender, EventArgs e)
        {
            if (mUsernameText.Text == "")
            {
                MessageBox.Show("Please provide a Twitch username");
                return;
            }
            else if (mPasswordText.Text == "")
            {
                MessageBox.Show("Please provide a Twitch password");
                return;
            }

            // Request a new auth token if the provided Twitch username has changed.
            // If have to request new auth token, don't begin ingest testing until callback
            // completes
            if (mUsernameText.Text != mTwitchUsername || mPasswordText.Text != mTwitchPassword)
            {
                if (!mBroadcastController.RequestAuthToken(mUsernameText.Text, mPasswordText.Text))
                {
                    mBeginIngestTest = false;
                }
                else
                {
                    mTwitchUsername = mUsernameText.Text;
                    mTwitchPassword = mPasswordText.Text;
                    mBeginIngestTest = true;
                }
            }
            else
            {
                BeginIngestTest();
            }
        }

        void mIngestTester_OnTestStateChanged(Twitch.Broadcast.IngestTester source, Twitch.Broadcast.IngestTester.TestState state)
        {
            mIngestTestStatusText.Text = "[" + (int)(mIngestTester.TotalProgress * 100) + "%] " + state.ToString();

            switch (state)
            {
                case Twitch.Broadcast.IngestTester.TestState.ConnectingToServer:
                {
                    mIngestTestStatusText.Text += ": " + source.CurrentServer.ServerName + "...";
                    break;
                }
                case Twitch.Broadcast.IngestTester.TestState.TestingServer:
                case Twitch.Broadcast.IngestTester.TestState.DoneTestingServer:
                {
                    mIngestTestStatusText.Text += ": " + source.CurrentServer.ServerName + "... " + source.CurrentServer.BitrateKbps + " kbps";
                    break;
                }
                case Twitch.Broadcast.IngestTester.TestState.Finished:
                {
                    String bestServerName = "";
                    float bestBitrate = 0;
                    Twitch.Broadcast.IngestServer curServer = null;

                    for (int i = 0; i < mIngestListListbox.Items.Count; i++)
                    {
                        curServer = ((IngestListEntry) mIngestListListbox.Items[i]).Server;
                        if (curServer.BitrateKbps > bestBitrate)
                        {
                            bestBitrate = curServer.BitrateKbps;
                            bestServerName = curServer.ServerName;
                        }
                    }
                    mBestServerLabel.Text = "Best server - " + bestServerName;

                    mSkipIngestServerButton.Enabled = false;
                    mCancelIngestTestButton.Enabled = false;

                    mIngestTester.OnTestStateChanged -= mIngestTester_OnTestStateChanged;
                    mIngestTester = null;
                    break;
                }
                case Twitch.Broadcast.IngestTester.TestState.Cancelled:
                {
                    mSkipIngestServerButton.Enabled = false;
                    mCancelIngestTestButton.Enabled = false;

                    mIngestTester.OnTestStateChanged -= mIngestTester_OnTestStateChanged;
                    mIngestTester = null;
                    break;
                }
                default:
                {
                    break;
                }
            }

            RefreshListbox(mIngestListListbox);
        }

        private void mSkipIngestServerButton_Click(object sender, EventArgs e)
        {
            if (mIngestTester == null)
            {
                return;
            }

            mIngestTester.SkipCurrentServer();
        }

        private void mCancelIngestTestButton_Click(object sender, EventArgs e)
        {
            if (mIngestTester == null)
            {
                return;
            }

            mIngestTester.Cancel();
        }

        private bool BeginIngestTest()
        {
            if (mIngestTester == null)
            {
                mIngestTester = mBroadcastController.StartIngestTest();
                if (mIngestTester != null)
                {
                    mCancelIngestTestButton.Enabled = true;
                    mSkipIngestServerButton.Enabled = true;
                    mIngestTester.OnTestStateChanged += mIngestTester_OnTestStateChanged;
                    return true;
                }
                mStartIngestTestButton.Enabled = true;
            }

            return false;
        }
    }
}
