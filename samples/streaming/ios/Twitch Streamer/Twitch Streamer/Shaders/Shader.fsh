//
//  Shader.fsh
//  Twitch Streamer
//
//  Created by Auston Stewart on 11/7/13.
//  Copyright (c) 2014 Justin.tv, Inc. All rights reserved.
//

varying lowp vec4 colorVarying;

void main()
{
    gl_FragColor = colorVarying;
}
