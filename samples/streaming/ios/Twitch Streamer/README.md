#Twitch Streaming Sample for iOS

- In addition to configuration and initialization of the Twitch SDK for iOS, this project demonstrates the use of a FBO to capture video frames from an OpenGL ES 2.0 / GLKit renderer, as well as audio capture from a rudimentary OpenAL controller.

#Requirements
- This project should be built using Xcode 5 and tested on devices running iOS 7.0 or later.

#Caveats
- CPU usage is high, especially on devices with high-density displays, as the frames captured are the size of screen and a couple buffer copies occur in the encoding pipeline.
- The rendering code in TWViewController doesn't follow best practices. Specifically, it reassigns the frame buffer's color buffer attachment after FBO rendering using a magic number and redraws the same geometry instead of presenting the captured FBO texture on a quad. This will be revised in future versions.
