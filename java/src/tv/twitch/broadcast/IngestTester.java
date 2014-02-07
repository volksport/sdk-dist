package tv.twitch.broadcast;

import java.util.ArrayList;
import java.util.List;

import tv.twitch.AuthToken;
import tv.twitch.ErrorCode;

/**
 * Performs ingest bandwidth testing to determine the best server a user should connect to for broadcasting.  This will fill in the BitrateKbps 
 * field of the given IngestServers.  Testing may take a while because there are several servers to test and each one may be tested for several
 * seconds.  You may want to display a progress bar each for the current server and overall progress.
 */
public class IngestTester
{
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

	public interface Listener
	{
		/**
		 * Event fired when the test changes states.
		 * @param source
		 * @param state
		 */
		void onIngestTestStateChanged(IngestTester source, TestState state);
	}
    
    protected Listener m_Listener = null;

    protected Stream m_Stream = null;
    protected IngestList m_IngestList = null;

    protected TestState m_TestState = TestState.Uninitalized;
    protected long m_TestDurationMilliseconds = 8000;
    protected long m_DelayBetweenServerTestsMilliseconds = 2000;
    protected long m_TotalSent = 0;
    protected RTMPState m_RTMPState = RTMPState.Invalid;
    protected VideoParams m_IngestTestVideoParams = null;
    protected AudioParams m_IngestTestAudioParams = null;
    protected long m_StartTimeMilliseconds = 0;
    protected List<FrameBuffer> m_IngestBuffers = null;
    protected boolean m_ServerTestSucceeded = false;
    protected IStreamCallbacks m_PreviousStreamCallbacks = null;
    protected IStatCallbacks m_PreviousStatCallbacks = null;
    protected IngestServer m_CurrentServer = null;
    protected boolean m_CancelTest = false;
    protected boolean m_SkipServer = false;
    protected int m_CurrentServerIndex = -1;
    protected int m_BufferIndex = 0;
    protected long m_LastTotalSent = 0;
    protected float m_TotalProgress = 0;
    protected float m_ServerProgress = 0;
    protected boolean m_Broadcasting = false;
    protected boolean m_WaitingForStartCallback = false;
    protected boolean m_WaitingForStopCallback = false;

    public void setListener(Listener listener)
    {
    	m_Listener = listener;
    }
    public Listener getListener()
    {
    	return m_Listener;
    }
    
    public TestState getState()
    {
        return m_TestState;
    }

    public IngestServer getCurrentServer()
    {
        return m_CurrentServer;
    }

    public IngestList getIngestList()
    {
        return m_IngestList;
    }

    public boolean getIsDone()
    {
        return m_TestState == TestState.Finished || m_TestState == TestState.Cancelled || m_TestState == TestState.Failed;
    }

    public long getTestDurationMilliseconds()
    {
        return m_TestDurationMilliseconds;
    }
    public void setTestDurationMilliseconds(long value)
    {
        m_TestDurationMilliseconds = value;
    }

    /**
     * The overall progress between [0,1].
     * @return
     */
    public float getTotalProgress()
    {
        return m_TotalProgress;
    }

    /**
     * The progress for the current server [0,1].
     * @return
     */
    public float getServerProgress()
    {
        return m_ServerProgress;
    }

    protected IStreamCallbacks streamCallbacks = new IStreamCallbacks()
    {
    	@Override
	    public void requestAuthTokenCallback(ErrorCode result, AuthToken authToken)
	    {
	    }
	
    	@Override
	    public void loginCallback(ErrorCode result, ChannelInfo channelInfo)
	    {
	    }
	
    	@Override
	    public void getIngestServersCallback(ErrorCode result, IngestList ingestList)
	    {
	    }
	
    	@Override
	    public void getUserInfoCallback(ErrorCode result, UserInfo userInfo)
	    {
	    }
	
    	@Override
	    public void getStreamInfoCallback(ErrorCode result, StreamInfo streamInfo)
	    {
	    }
	
    	@Override
	    public void getArchivingStateCallback(ErrorCode result, ArchivingState state)
	    {
	    }
	
    	@Override
	    public void runCommercialCallback(ErrorCode result)
	    {
	    }
	
    	@Override
	    public void setStreamInfoCallback(ErrorCode result)
	    {
	    }
	
    	@Override
	    public void getGameNameListCallback(ErrorCode result, GameInfoList list)
	    {
	    }
	
    	@Override
	    public void bufferUnlockCallback(long address)
	    {
	    }
	    
    	@Override
	    public void startCallback(ErrorCode ret)
	    {
        	//System.out.println("IngestTester.startCallback - " + m_CurrentServer.serverName + ": " + ret.toString());	        

        	m_WaitingForStartCallback = false;
	
	        // started
	        if (ErrorCode.succeeded(ret))
	        {
	        	m_Broadcasting = true;
	            m_StartTimeMilliseconds = System.currentTimeMillis();
	        	
	            setTestState(TestState.ConnectingToServer);	
	        }
	        // failed to start so skip it
	        else
	        {
	            m_ServerTestSucceeded = false;
	            setTestState(TestState.DoneTestingServer);
	        }
	    }
	
    	@Override
	    public void stopCallback(ErrorCode ret)
	    {
        	//System.out.println("IngestTester.stopCallback - " + m_CurrentServer.serverName + ": " + ret.toString());

        	if (ErrorCode.failed(ret))
        	{
        		// this should never happen and there's not really any way to recover
        		System.out.println("IngestTester.stopCallback failed to stop - " + m_CurrentServer.serverName + ": " + ret.toString());
        	}
        	
    		m_WaitingForStopCallback = false;
        	m_Broadcasting = false;
        	
	        setTestState(TestState.DoneTestingServer);
	        
	        m_CurrentServer = null;
	        
	        if (m_CancelTest)
	        {
	        	setTestState(TestState.Cancelling);
	        }
	    }
	    
    	@Override
	    public void sendActionMetaDataCallback(ErrorCode ret)
	    {
	    }
	    
    	@Override
	    public void sendStartSpanMetaDataCallback(ErrorCode ret)
	    {
	    }
	    
    	@Override
	    public void sendEndSpanMetaDataCallback(ErrorCode ret)
	    {
	    }
    };

    protected IStatCallbacks statCallbacks = new IStatCallbacks()
    {
	    public void statCallback(StatType type, long data)
	    {
	        switch (type)
	        {
	            case TTV_ST_RTMPSTATE:
	                m_RTMPState = RTMPState.lookupValue((int)data);
	                break;
	
	            case TTV_ST_RTMPDATASENT:
	                m_TotalSent = data;
	                break;
	        }
	    }
    };


    public IngestTester(Stream stream, IngestList ingestList)
    {
        m_Stream = stream;
        m_IngestList = ingestList;
    }
    
    /**
     * Begins the ingest testing.
     */
    public void start()
    {
        // can only run it once per instance
        if (m_TestState != TestState.Uninitalized)
        {
            return;
        }

        m_CurrentServerIndex = 0;
        m_CancelTest = false;
        m_SkipServer = false;
        m_Broadcasting = false;
        m_WaitingForStartCallback = false;
        m_WaitingForStopCallback = false;
        
        m_PreviousStatCallbacks = m_Stream.getStatCallbacks();
        m_Stream.setStatCallbacks(statCallbacks);

        m_PreviousStreamCallbacks = m_Stream.getStreamCallbacks();
        m_Stream.setStreamCallbacks(streamCallbacks);

        m_IngestTestVideoParams = new VideoParams();
        m_IngestTestVideoParams.targetFps = tv.twitch.broadcast.Constants.TTV_MAX_FPS;
        m_IngestTestVideoParams.maxKbps = tv.twitch.broadcast.Constants.TTV_MAX_BITRATE;
        m_IngestTestVideoParams.outputWidth = 1280;
        m_IngestTestVideoParams.outputHeight = 720;
        m_IngestTestVideoParams.pixelFormat = PixelFormat.TTV_PF_BGRA;
        m_IngestTestVideoParams.encodingCpuUsage = EncodingCpuUsage.TTV_ECU_HIGH;
        m_IngestTestVideoParams.disableAdaptiveBitrate = true;
        m_IngestTestVideoParams.verticalFlip = false;
        
        m_Stream.getDefaultParams(m_IngestTestVideoParams);

        m_IngestTestAudioParams = new AudioParams();
        m_IngestTestAudioParams.audioEnabled = false;
        m_IngestTestAudioParams.enableMicCapture = false;
        m_IngestTestAudioParams.enablePlaybackCapture = false;
        m_IngestTestAudioParams.enablePassthroughAudio = false;

        m_IngestBuffers = new ArrayList<FrameBuffer>();

        // allocate some buffers
        int numFrames = 3;

        for (int i = 0; i < numFrames; ++i)
        {
        	FrameBuffer buffer = m_Stream.allocateFrameBuffer(m_IngestTestVideoParams.outputWidth * m_IngestTestVideoParams.outputHeight * 4);
            if (!buffer.getIsValid())
            {
            	cleanup();
            	setTestState(TestState.Failed);
                return;
            }

            m_IngestBuffers.add(buffer);

            m_Stream.randomizeFrameBuffer(buffer);
        }

        setTestState(TestState.Starting);
        
        m_StartTimeMilliseconds = System.currentTimeMillis();
    }

    /**
     * Updates the internal state of the tester.  This should be called as frequently as possible.
     */
    public void update()
    {
        if (this.getIsDone() || m_TestState == TestState.Uninitalized)
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
            case Starting:
            case DoneTestingServer:
            {
                // cleanup the previous server test
                if (m_CurrentServer != null)
                {
                    if (m_SkipServer || !m_ServerTestSucceeded)
                    {
                        m_CurrentServer.bitrateKbps = 0;
                    }

                    stopServerTest(m_CurrentServer);
                }
                // start the next test
                else
                {
                	m_StartTimeMilliseconds = 0;
                    m_SkipServer = false;
                    m_ServerTestSucceeded = true;

                    if (m_TestState != TestState.Starting)
                    {
                        m_CurrentServerIndex++;
                    }

                    // start the next server test
                    if (m_CurrentServerIndex < m_IngestList.getServers().length)
                    {
                        m_CurrentServer = m_IngestList.getServers()[m_CurrentServerIndex];
                        startServerTest(m_CurrentServer);
                    }
                    // done testing all servers
                    else
                    {
                        setTestState(TestState.Finished);
                    }
                }
                
            	break;
            }
            case ConnectingToServer:
            case TestingServer:
            {
                updateServerTest(m_CurrentServer);
                break;
            }
            case Cancelling:
            {
            	setTestState(TestState.Cancelled);
                break;
            }
            default:
            {
                break;
            }
        }

        updateProgress();

        // test finished
        if (m_TestState == TestState.Cancelled || m_TestState == TestState.Finished)
        {
        	cleanup();
        }
    }

    /**
     * Skips the server currently being tested.  It will leave a 0 value in the bitrate field of the server.
     */
    public void skipCurrentServer()
    {
        if (this.getIsDone())
        {
            return;
        }
        
        switch (m_TestState)
        {
            case ConnectingToServer:
            case TestingServer:
                m_SkipServer = true;
                break;
            default:
                return;
        }
    }

    /**
     * Cancels the ingest testing.  This may leave invalid values in any servers which did not complete testing.
     */
    public void cancel()
    {
        if (this.getIsDone() || m_CancelTest)
        {
            return;
        }
        
    	m_CancelTest = true;
    	
    	if (m_CurrentServer != null)
    	{
    		m_CurrentServer.bitrateKbps = 0;
    	}
    }

    protected boolean startServerTest(IngestServer server)
    {
        // reset the test
        m_ServerTestSucceeded = true;
        m_TotalSent = 0;
        m_RTMPState = RTMPState.Idle;
        m_CurrentServer = server;

        // start the stream
        m_WaitingForStartCallback = true;
        setTestState(TestState.ConnectingToServer);
        
        ErrorCode ret = m_Stream.start(m_IngestTestVideoParams, m_IngestTestAudioParams, server, StartFlags.TTV_Start_BandwidthTest, true);
        if (ErrorCode.failed(ret))
        {
        	m_WaitingForStartCallback = false;
            m_ServerTestSucceeded = false;
            setTestState(TestState.DoneTestingServer);
            return false;
        }

        // the amount of data sent before the test of this server starts
        m_LastTotalSent = m_TotalSent;

        server.bitrateKbps = 0;
        m_BufferIndex = 0;

        return true;
    }

    protected void stopServerTest(IngestServer server)
    {
    	//System.out.println("stopServerTest: " + server.serverName);
    	
    	if (m_WaitingForStartCallback)
    	{
    		// wait for the start callback and do the stop after that comes in
    		m_SkipServer = true;
    	}
    	else if (m_Broadcasting)
    	{
    		m_WaitingForStopCallback = true;
	        
	        ErrorCode ec = m_Stream.stop(true);
	        if (ErrorCode.failed(ec))
	        {
	        	// this should never happen so fake the callback to indicate it's stopped
	        	streamCallbacks.stopCallback(ErrorCode.TTV_EC_SUCCESS);
	        	
	        	System.out.println("Stop failed: " + ec.toString());
	        }

	        m_Stream.pollStats();
    	}
    	else
    	{
    		// simulate a stop callback
    		streamCallbacks.stopCallback(ErrorCode.TTV_EC_SUCCESS);
    	}
    }

    protected long elapsedMilliseconds()
    {
    	return System.currentTimeMillis() - m_StartTimeMilliseconds;
    }
    
    protected void updateProgress()
    {
        float elapsed = (float)elapsedMilliseconds();

        switch (m_TestState)
        {
            case Uninitalized:
            case Starting:
            case ConnectingToServer:
            case Finished:
            case Cancelled:
            case Failed:
            {
                m_ServerProgress = 0.0f;
                break;
            }
            case DoneTestingServer:
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
            case Finished:
            case Cancelled:
            case Failed:
            {
                m_TotalProgress = 1.0f;
                break;
            }
            default:
            {
                m_TotalProgress = (float)m_CurrentServerIndex / (float)m_IngestList.getServers().length;
                m_TotalProgress += m_ServerProgress / m_IngestList.getServers().length;
                break;
            }
        }
    }

    protected boolean updateServerTest(IngestServer server)
    {
        if (m_SkipServer || m_CancelTest || elapsedMilliseconds() >= m_TestDurationMilliseconds)
        {
            setTestState(TestState.DoneTestingServer);
            return true;
        }

        if (m_WaitingForStartCallback || m_WaitingForStopCallback)
        {
            return true;
        }

        ErrorCode ret = m_Stream.submitVideoFrame(m_IngestBuffers.get(m_BufferIndex));
        if (ErrorCode.failed(ret))
        {
            m_ServerTestSucceeded = false;
            setTestState(TestState.DoneTestingServer);
            return false;
        }

        m_BufferIndex = (m_BufferIndex+1) % m_IngestBuffers.size();

        m_Stream.pollStats();

        // connected and sending video
        if (m_RTMPState == RTMPState.SendVideo)
        {
            setTestState(TestState.TestingServer);

            long elapsed = elapsedMilliseconds();
            if (elapsed > 0 && m_TotalSent > m_LastTotalSent)
            {
            	server.bitrateKbps = (float)(m_TotalSent * 8) / (float)elapsedMilliseconds();
                m_LastTotalSent = m_TotalSent;
            }
        }

        return true;
    }

    protected void cleanup()
    {
        m_CurrentServer = null;

        // free the buffers
        if (m_IngestBuffers != null)
        {
	        for (int i = 0; i < m_IngestBuffers.size(); ++i)
	        {
	        	m_IngestBuffers.get(i).free();
	        }
	        
	        m_IngestBuffers = null;
        }

        if (m_Stream.getStatCallbacks() == statCallbacks)
        {
	        m_Stream.setStatCallbacks(m_PreviousStatCallbacks);
	        m_PreviousStatCallbacks = null;
        }
        
        if (m_Stream.getStreamCallbacks() == streamCallbacks)
        {
	        m_Stream.setStreamCallbacks(m_PreviousStreamCallbacks);
	        m_PreviousStreamCallbacks = null;
        }
    }

    protected void setTestState(TestState state)
    {
        if (state == m_TestState)
        {
            return;
        }

        m_TestState = state;

        if (m_Listener != null)
        {
        	m_Listener.onIngestTestStateChanged(this, state);
        }
    }
}
