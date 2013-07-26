#Twitch Unity Plugin - Change history

Note: These updates are Unity-specific and are in addition to updates that can be found in the native SDK releases.

####July 25, 2013  
- Removed the need for curl-ca-bundle.crt.  The resource has been removed from the Unity sample project and should be removed from your project as well.  
- Removed the need for libcurl-ttv.dll, libeay32-ttv.dll and ssleay32-ttv.dll.  These dependencies should be removed from your Assets/Plugins/* folders if you've copied them there.  
- Refactored the BroadcastController and ChatController inheritance hierarchy.  Added BroadcastControllerMonoBehaviour.cs which needs to be added to your project.  
- Added support for 32-bit Mac OS X.  

####July 5, 2013  
- Fixed an issue with IngestTester which may have caused it to fail to start.  
- Reduced the amount of time frames will queue for if your internet connection is too slow for your broadcast settings.  

####June 20, 2013  
- Fixed compile and linking issues when building for iOS or WebPlayer.  
- The ca-cert-bundle file is now a resource and does not need to be copied manually when performing a build.  It is saved out to disk when needed in Application.temporaryCachePath.  
- Support has been added so that you can build your game as a 64-bit executable.  
- The plugin directory has been reorganized a bit to accommodate building as a 64-bit executable.  TwitchSdkWrapper.dll is now platform independent and found directly in Assets/Plugins.  
- Twitch dependency libraries no longer need to be located in the root of the project.  We now support multiple versions of them (for 32 and 64-bit) under Assets/Plugins/x86 and Assets/Plugins/x86_64 and the appropriate ones will be loaded when needed.  
- Twitch dependency libraries no longer need to be copied manually into the built game, the appropriate version will be copied out of Assets/Plugins/<platform> when making a build and placed in the correct location.  
- The meta-data API on BroadcastController has been updated.  
- BroadcastController, ChatController and IngestTester have changed namespaces.  
- There is an example of how to change scenes without dropping the broadcast.  
- Added more documentation to some C# files.  
