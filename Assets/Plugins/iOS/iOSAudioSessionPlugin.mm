// Experiment (branch ios-aec-test): ask iOS for its voice-chat audio processing (echo cancellation) so Stu's
// voice from the loudspeaker isn't picked up by the mic and sent back to the ElevenLabs agent.
// Called from StuConversation (Awake and when a conversation starts). Logs the resulting session state so the
// Xcode console shows whether iOS kept PlayAndRecord + VoiceChat.
#import <AVFoundation/AVFoundation.h>

extern "C" {
    void _EnableIOSVoiceChatAEC() {
        AVAudioSession *session = [AVAudioSession sharedInstance];
        NSError *error = nil;

        // PlayAndRecord allows simultaneous mic recording and speaker playback
        if (![session setCategory:AVAudioSessionCategoryPlayAndRecord
                      withOptions:AVAudioSessionCategoryOptionDefaultToSpeaker | AVAudioSessionCategoryOptionAllowBluetoothHFP
                            error:&error])
            NSLog(@"[StuAEC] setCategory failed: %@", error.localizedDescription);

        // VoiceChat mode enables Apple's voice processing (echo cancellation)
        error = nil;
        if (![session setMode:AVAudioSessionModeVoiceChat error:&error])
            NSLog(@"[StuAEC] setMode failed: %@", error.localizedDescription);

        error = nil;
        if (![session setActive:YES error:&error])
            NSLog(@"[StuAEC] setActive failed: %@", error.localizedDescription);

        AVAudioSessionRouteDescription *route = session.currentRoute;
        NSLog(@"[StuAEC] category=%@ mode=%@ sampleRate=%.0f in=%@ out=%@",
              session.category, session.mode, session.sampleRate,
              route.inputs.firstObject.portType, route.outputs.firstObject.portType);
    }
}
