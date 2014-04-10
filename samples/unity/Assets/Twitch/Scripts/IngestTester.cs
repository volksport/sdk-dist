using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Twitch.Broadcast
{
    /// <summary>
    /// Performs ingest bandwidth testing to determine the best server a user should connect to for broadcasting.  This will fill in the BitrateKbps 
    /// field of the given IngestServers.  Testing may take a while because there are several servers to test and each one may be tested for several
    /// seconds.  This class cannot be used independently from the BroadcastController (BC) and is single-use only.  Begin a new test via BC.StartIngestTest().
    /// 
    /// The IngestTester (IT) will fire events to indicate state changes such as the starting and stopping of a server test and completion of all tests.
    /// 
    /// Progress of the overall test and of the current server can be gotten from TotalProgress and ServerProgress, respectively.  You may want to display a progress bar for each.
    /// 
    /// The whole test can be cancelled via Cancel().  The current server test can be skipped via SkipCurrentServer() which will trigger the testing of the next server.
    /// </summary>
    public class IngestTester
    {
        public delegate void TestStateChangedDelegate(IngestTester source, TestState state);

        public enum TestState
        {
            Uninitalized,
            Starting,
            ConnectingToServer,
            TestingServer,
            DoneTestingServer,
            Finished,
            Cancelling,
            Cancelled,
            Failed
        }

        /// <summary>
        /// Event fired when the test changes states.
        /// </summary>
        public event TestStateChangedDelegate OnTestStateChanged;

        protected const bool k_AsyncStartStop = true;

        protected BroadcastApi m_Stream = null;
        protected IngestServer[] m_IngestList = null;

        protected TestState m_TestState = TestState.Uninitalized;
        protected long m_TestDurationMilliseconds = 8000;
        protected long m_DelayBetweenServerTestsMilliseconds = 2000;
        protected UInt64 m_TotalSent = 0;
        protected RTMPState m_RTMPState = RTMPState.Invalid;
        protected VideoParams m_IngestTestVideoParams = null;
        protected AudioParams m_IngestTestAudioParams = null;
        protected System.Diagnostics.Stopwatch m_Timer = new System.Diagnostics.Stopwatch();
        protected List<UIntPtr> m_IngestBuffers = null;
        protected bool m_ServerTestSucceeded = false;
        protected IBroadcastApiListener m_PreviousStreamCallbacks = null;
        protected IStatsListener m_PreviousStatCallbacks = null;
        protected IngestServer m_CurrentServer = null;
        protected bool m_CancelTest = false;
        protected bool m_SkipServer = false;
        protected int m_CurrentServerIndex = -1;
        protected int m_BufferIndex = 0;
        protected UInt64 m_LastTotalSent = 0;
        protected float m_TotalProgress = 0;
        protected float m_ServerProgress = 0;
        protected bool m_WaitingForStartCallback = false;
        protected bool m_WaitingForStopCallback = false;
        protected bool m_Broadcasting = false;

        protected StreamCallbackListener m_StreamCallbackListener = null;
        protected StatCallbackListener m_StatCallbackListener = null;

        #region Properties

        public TestState State
        {
            get { return m_TestState; }
        }

        /// <summary>
        /// The server currently being tested.  Will be null at the beginning and end of testing.
        /// </summary>
        public IngestServer CurrentServer
        {
            get { return m_CurrentServer; }
        }

        /// <summary>
        /// The list being tested.
        /// </summary>
        public IngestServer[] IngestList
        {
            get { return m_IngestList; }
        }

        /// <summary>
        /// Whether or not all testing is complete.
        /// </summary>
        public bool IsDone
        {
            get { return m_TestState == TestState.Finished || m_TestState == TestState.Cancelled || m_TestState == TestState.Failed; }
        }

        /// <summary>
        /// The number of milliseconds to test each server.  The longer the time, the more accurate the testing.
        /// </summary>
        public long TestDurationMilliseconds
        {
            get { return m_TestDurationMilliseconds; }
            set { m_TestDurationMilliseconds = value; }
        }

        /// <summary>
        /// The overall progress between [0,1].
        /// </summary>
        public float TotalProgress
        {
            get { return m_TotalProgress; }
        }

        /// <summary>
        /// The progress for the current server [0,1].
        /// </summary>
        public float ServerProgress
        {
            get { return m_ServerProgress; }
        }

        #endregion

        protected abstract class IngestTesterAccess
        {
            protected IngestTester m_IngestTester;

            protected IngestTesterAccess(IngestTester tester)
            {
                m_IngestTester = tester;
            }

            protected bool WaitingForStartCallback
            {
                get { return m_IngestTester.m_WaitingForStartCallback; }
                set { m_IngestTester.m_WaitingForStartCallback = value; }
            }

            protected bool WaitingForStopCallback
            {
                get { return m_IngestTester.m_WaitingForStopCallback; }
                set { m_IngestTester.m_WaitingForStopCallback = value; }
            }

            protected bool Broadcasting
            {
                get { return m_IngestTester.m_Broadcasting; }
                set { m_IngestTester.m_Broadcasting = value; }
            }

            protected bool ServerTestSucceeded
            {
                get { return m_IngestTester.m_ServerTestSucceeded; }
                set { m_IngestTester.m_ServerTestSucceeded = value; }
            }

            protected bool CancelTest
            {
                get { return m_IngestTester.m_CancelTest; }
                set { m_IngestTester.m_CancelTest = value; }
            }

            protected System.Diagnostics.Stopwatch Timer
            {
                get { return m_IngestTester.m_Timer; }
            }

            protected RTMPState RTMPState
            {
                get { return m_IngestTester.m_RTMPState; }
                set { m_IngestTester.m_RTMPState = value; }
            }

            protected UInt64 TotalSent
            {
                get { return m_IngestTester.m_TotalSent; }
                set { m_IngestTester.m_TotalSent = value; }
            }

            protected IngestServer CurrentServer
            {
                get { return m_IngestTester.m_CurrentServer; }
                set { m_IngestTester.m_CurrentServer = value; }
            }

            protected BroadcastApi Api
            {
                get { return m_IngestTester.m_Stream; }
            }

            protected void SetTestState(TestState state)
            {
                m_IngestTester.SetTestState(state);
            }
        }

        protected class StreamCallbackListener : IngestTesterAccess, IBroadcastApiListener
        {
            public StreamCallbackListener(IngestTester tester)
                : base(tester)
            {
            }

            void IBroadcastApiListener.RequestAuthTokenCallback(ErrorCode result, AuthToken authToken)
            {
            }

            void IBroadcastApiListener.LoginCallback(ErrorCode result, ChannelInfo channelInfo)
            {
            }

            void IBroadcastApiListener.GetIngestServersCallback(ErrorCode result, IngestList ingestList)
            {
            }

            void IBroadcastApiListener.GetUserInfoCallback(ErrorCode result, UserInfo userInfo)
            {
            }

            void IBroadcastApiListener.GetStreamInfoCallback(ErrorCode result, StreamInfo streamInfo)
            {
            }

            void IBroadcastApiListener.GetArchivingStateCallback(ErrorCode result, ArchivingState state)
            {
            }

            void IBroadcastApiListener.RunCommercialCallback(ErrorCode result)
            {
            }

            void IBroadcastApiListener.SetStreamInfoCallback(ErrorCode result)
            {
            }

            void IBroadcastApiListener.GetGameNameListCallback(ErrorCode result, GameInfoList list)
            {
            }

            void IBroadcastApiListener.BufferUnlockCallback(UIntPtr buffer)
            {
            }

            void IBroadcastApiListener.StartCallback(ErrorCode ret)
            {
                this.WaitingForStartCallback = false;

                // started
                if (Error.Succeeded(ret))
                {
                    this.Broadcasting = true;
                    this.Timer.Reset();
                    this.Timer.Start();

                    SetTestState(TestState.ConnectingToServer);
                }
                // failed to start so skip it
                else
                {
                    this.ServerTestSucceeded = false;
                    SetTestState(TestState.DoneTestingServer);
                }
            }

            void IBroadcastApiListener.StopCallback(ErrorCode ret)
            {
        	    if (Error.Failed(ret))
        	    {
        		    // this should never happen and there's not really any way to recover
        		    System.Diagnostics.Debug.WriteLine("IngestTester.stopCallback failed to stop - " + this.CurrentServer.ServerName + ": " + ret.ToString());
        	    }
        	
    		    this.WaitingForStopCallback = false;
        	    this.Broadcasting = false;
        	
	            SetTestState(TestState.DoneTestingServer);
	        
	            this.CurrentServer = null;
	        
	            if (this.CancelTest)
	            {
	        	    SetTestState(TestState.Cancelling);
	            }
            }

            void IBroadcastApiListener.SendActionMetaDataCallback(ErrorCode ret)
            {
            }

            void IBroadcastApiListener.SendStartSpanMetaDataCallback(ErrorCode ret)
            {
            }

            void IBroadcastApiListener.SendEndSpanMetaDataCallback(ErrorCode ret)
            {
            }
        }

        protected class StatCallbackListener : IngestTesterAccess, IStatsListener
        {
            public StatCallbackListener(IngestTester tester)
                : base(tester)
            {
            }

            void IStatsListener.StatCallback(StatType type, ulong data)
            {
                switch (type)
                {
                    case StatType.TTV_ST_RTMPSTATE:
                        this.RTMPState = (RTMPState)data;
                        break;

                    case StatType.TTV_ST_RTMPDATASENT:
                        this.TotalSent = data;
                        break;
                }
            }
        }

        internal IngestTester(BroadcastApi stream, IngestList ingestList)
        {
            m_Stream = stream;
            m_IngestList = ingestList.Servers;
        }

        internal IngestTester(BroadcastApi stream, IngestServer[] ingestList)
        {
            m_Stream = stream;
            m_IngestList = ingestList;
        }

        /// <summary>
        /// Begins the ingest testing.
        /// </summary>
        public void Start()
        {
            // can only run it once per instance
            if (m_TestState != TestState.Uninitalized)
            {
                return;
            }

            m_StreamCallbackListener = new StreamCallbackListener(this);
            m_StatCallbackListener = new StatCallbackListener(this);

            m_CurrentServerIndex = 0;
            m_CancelTest = false;
            m_SkipServer = false;
            m_Broadcasting = false;
            m_WaitingForStartCallback = false;
            m_WaitingForStopCallback = false;

            m_PreviousStatCallbacks = m_Stream.StatsListener;
            m_Stream.StatsListener = m_StatCallbackListener;

            m_PreviousStreamCallbacks = m_Stream.BroadcastApiListener;
            m_Stream.BroadcastApiListener = m_StreamCallbackListener;

            m_IngestTestVideoParams = new VideoParams();
            m_IngestTestVideoParams.TargetFps = Twitch.Broadcast.Constants.TTV_MAX_FPS;
            m_IngestTestVideoParams.MaxKbps = Twitch.Broadcast.Constants.TTV_MAX_BITRATE;
            m_IngestTestVideoParams.OutputWidth = 1280;
            m_IngestTestVideoParams.OutputHeight = 720;
            m_IngestTestVideoParams.PixelFormat = PixelFormat.TTV_PF_BGRA;
            m_IngestTestVideoParams.DisableAdaptiveBitrate = true;
            m_IngestTestVideoParams.VerticalFlip = false;

            m_Stream.GetDefaultParams(m_IngestTestVideoParams);

            m_IngestTestAudioParams = new AudioParams();
            m_IngestTestAudioParams.AudioEnabled = false;
            m_IngestTestAudioParams.EnableMicCapture = false;
            m_IngestTestAudioParams.EnablePlaybackCapture = false;
            m_IngestTestAudioParams.EnablePassthroughAudio = false;

            m_IngestBuffers = new List<UIntPtr>();

            // allocate some buffers
            int numFrames = 3;

            for (int i = 0; i < numFrames; ++i)
            {
                UIntPtr buffer = UIntPtr.Zero;
                uint size = m_IngestTestVideoParams.OutputWidth * m_IngestTestVideoParams.OutputHeight * 4;

                m_Stream.AllocateFrameBuffer(size, out buffer);

                if (buffer == UIntPtr.Zero)
                {
                    Cleanup();
                    SetTestState(TestState.Failed);
                    return;
                }

                m_Stream.RandomizeFrameBuffer(buffer, size);
                
                m_IngestBuffers.Add(buffer);
            }

            SetTestState(TestState.Starting);

            m_Timer.Reset();
            m_Timer.Start();
        }

        /// <summary>
        /// Updates the internal state of the tester.  This should be called as frequently as possible.
        /// </summary>
        public void Update()
        {
            if (this.IsDone || m_TestState == TestState.Uninitalized)
            {
                return;
            }

            // nothing to be done while waiting for a callback
            if (m_WaitingForStartCallback || m_WaitingForStopCallback)
            {
                return;
            }

            switch (m_TestState)
            {
                case TestState.Starting:
                case TestState.DoneTestingServer:
                {
                    // cleanup the previous server test
                    if (m_CurrentServer != null)
                    {
                        if (m_SkipServer || !m_ServerTestSucceeded)
                        {
                            m_CurrentServer.BitrateKbps = 0;
                        }

                        StopServerTest(m_CurrentServer);
                    }
                    // start the next test
                    else
                    {
                        m_Timer.Stop();
                        m_Timer.Reset();
                        m_SkipServer = false;
                        m_ServerTestSucceeded = true;

                        if (m_TestState != TestState.Starting)
                        {
                            m_CurrentServerIndex++;
                        }

                        // start the next server test
                        if (m_CurrentServerIndex < m_IngestList.Length)
                        {
                            m_CurrentServer = m_IngestList[m_CurrentServerIndex];
                            StartServerTest(m_CurrentServer);
                        }
                        // done testing all servers
                        else
                        {
                            SetTestState(TestState.Finished);
                        }
                    }
                
            	    break;
                }
                case TestState.ConnectingToServer:
                case TestState.TestingServer:
                {
                    UpdateServerTest(m_CurrentServer);
                    break;
                }
                case TestState.Cancelling:
                {
            	    SetTestState(TestState.Cancelled);
                    break;
                }
                default:
                {
                    break;
                }
            }

            UpdateProgress();

            // test finished
            if (m_TestState == TestState.Cancelled || m_TestState == TestState.Finished)
            {
        	    Cleanup();
            }
        }

        /// <summary>
        /// Skips the server currently being tested.  It will leave a 0 value in the bitrate field of the server.
        /// </summary>
        public void SkipCurrentServer()
        {
            if (this.IsDone)
            {
                return;
            }

            switch (m_TestState)
            {
                case TestState.ConnectingToServer:
                case TestState.TestingServer:
                    m_SkipServer = true;
                    break;
                default:
                    return;
            }
        }

        /// <summary>
        /// Cancels the ingest testing.  This may leave invalid values in any servers which did not complete testing.
        /// </summary>
        public void Cancel()
        {
            if (this.IsDone)
            {
                return;
            }
    
            m_CancelTest = true;

            if (m_CurrentServer != null)
            {
                m_CurrentServer.BitrateKbps = 0;
            }
        }

        protected bool StartServerTest(IngestServer server)
        {
            // reset the test
            m_ServerTestSucceeded = true;
            m_TotalSent = 0;
            m_RTMPState = RTMPState.Idle;
            m_CurrentServer = server;

            // start the stream
            m_WaitingForStartCallback = true;
            SetTestState(TestState.ConnectingToServer);

            ErrorCode ret = m_Stream.Start(m_IngestTestVideoParams, m_IngestTestAudioParams, server, StartFlags.TTV_Start_BandwidthTest, true);
            if (Error.Failed(ret))
            {
                m_WaitingForStartCallback = false;
                m_ServerTestSucceeded = false;
                SetTestState(TestState.DoneTestingServer);
                return false;
            }

            // the amount of data sent before the test of this server starts
            m_LastTotalSent = m_TotalSent;

            server.BitrateKbps = 0;
            m_BufferIndex = 0;

            return true;
        }

        protected void StopServerTest(IngestServer server)
        {
    	    if (m_WaitingForStartCallback)
    	    {
    		    // wait for the start callback and do the stop after that comes in
    		    m_SkipServer = true;
    	    }
    	    else if (m_Broadcasting)
    	    {
    		    m_WaitingForStopCallback = true;
	        
	            ErrorCode ec = m_Stream.Stop(true);
	            if (Error.Failed(ec))
	            {
	        	    // this should never happen so fake the callback to indicate it's stopped
                    (m_StreamCallbackListener as IBroadcastApiListener).StopCallback(ErrorCode.TTV_EC_SUCCESS);
	        	
	        	    System.Diagnostics.Debug.WriteLine("Stop failed: " + ec.ToString());
	            }

	            m_Stream.PollStats();
    	    }
    	    else
    	    {
    		    // simulate a stop callback
                (m_StreamCallbackListener as IBroadcastApiListener).StopCallback(ErrorCode.TTV_EC_SUCCESS);
    	    }
        }

        protected void UpdateProgress()
        {
            float elapsed = (float)m_Timer.ElapsedMilliseconds;

            switch (m_TestState)
            {
                case TestState.Uninitalized:
                case TestState.Starting:
                case TestState.ConnectingToServer:
                case TestState.Finished:
                case TestState.Cancelled:
                case TestState.Failed:
                {
                    m_ServerProgress = 0.0f;
                    break;
                }
                case TestState.DoneTestingServer:
                {
                    m_ServerProgress = 1.0f;
                    break;
                }
                default:
                {
                    m_ServerProgress = elapsed / (float)m_TestDurationMilliseconds;
                    break;
                }
            }

            switch (m_TestState)
            {
                case TestState.Finished:
                case TestState.Cancelled:
                case TestState.Failed:
                {
                    m_TotalProgress = 1.0f;
                    break;
                }
                default:
                {
                    m_TotalProgress = (float)m_CurrentServerIndex / (float)m_IngestList.Length;
                    m_TotalProgress += m_ServerProgress / m_IngestList.Length;
                    break;
                }
            }
        }

        protected bool UpdateServerTest(IngestServer server)
        {
            if (m_SkipServer || m_CancelTest || m_Timer.ElapsedMilliseconds >= m_TestDurationMilliseconds)
            {
                SetTestState(TestState.DoneTestingServer);
                return true;
            }

            if (m_WaitingForStartCallback || m_WaitingForStopCallback)
            {
                return true;
            }

            ErrorCode ret = m_Stream.SubmitVideoFrame(m_IngestBuffers[m_BufferIndex]);
            if (Error.Failed(ret))
            {
                m_ServerTestSucceeded = false;
                SetTestState(TestState.DoneTestingServer);
                return false;
            }

            m_BufferIndex = (m_BufferIndex + 1) % m_IngestBuffers.Count;

            m_Stream.PollStats();

            // connected and sending video
            if (m_RTMPState == RTMPState.SendVideo)
            {
                SetTestState(TestState.TestingServer);

                long elapsed = m_Timer.ElapsedMilliseconds;
                if (elapsed > 0 && m_TotalSent > m_LastTotalSent)
                {
                    server.BitrateKbps = (float)(m_TotalSent * 8) / (float)(m_Timer.ElapsedMilliseconds);
                    m_LastTotalSent = m_TotalSent;
                }
            }

            return true;
        }

        protected void Cleanup()
        {
            m_CurrentServer = null;

            // free the buffers
            if (m_IngestBuffers != null)
            {
                for (int i = 0; i < m_IngestBuffers.Count; ++i)
                {
                    m_Stream.FreeFrameBuffer(m_IngestBuffers[i]);
                }

                m_IngestBuffers = null;
            }

            if (m_Stream.StatsListener == m_StatCallbackListener)
            {
                m_Stream.StatsListener = m_PreviousStatCallbacks;
                m_PreviousStatCallbacks = null;
            }

            if (m_Stream.BroadcastApiListener == m_StreamCallbackListener)
            {
                m_Stream.BroadcastApiListener = m_PreviousStreamCallbacks;
                m_PreviousStreamCallbacks = null;
            }
        }

        protected void SetTestState(TestState state)
        {
            if (state == m_TestState)
            {
                return;
            }

            m_TestState = state;

            if (OnTestStateChanged != null)
            {
                OnTestStateChanged(this, state);
            }
        }
    }
}
