using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System.Threading.Tasks;
using FM.IceLink.WebRTC;
using Xamarin.WebRTC.Mobile.Android.Codecs.VP8;
using Xamarin.WebRTC.Mobile.Android.Codecs.Opus;
using FM.IceLink;
using Newtonsoft.Json;
using Xamarin.WebRTC.Mobile;

namespace Xamarin.WebRTC.Mobile.Droid
{
    public class ConferenceService
    {
        private const string STUN_TURN_URL = "demo.icelink.fm:3478";
        private readonly SignalingService _signalingService;
        private string _roomName;
        private LocalMediaStream _localMedia;
        private Conference _conference;
        private AndroidLayoutManager _layoutManager;
        private View _senderLocalVideoControl;
        private OpusEchoCanceller _opusEchoCanceller;


        public ConferenceService()
        {
            _signalingService = new SignalingService();
            _signalingService.OnNewConnection += (s, a) =>
            {
                ReceiveNewPeer(a.PeerId);
            };

            _signalingService.OnNewOfferAnswer += (s, a) =>
            {
                ReceiveOfferAnswer(a.PeerId, a.Json);
            };

            _signalingService.OnNewCandidate += (s, a) =>
            {
                ReceiveCandidate(a.PeerId, a.Json);
            };

            RegisterCodecs();
            InitJavaLibs();
        }

        /// <summary>
        /// Register VP8 and Opus as our codecs for video and audio
        /// </summary>
        private void RegisterCodecs()
        {
            VideoStream.RegisterCodec("VP8", () =>
            {
                return new Vp8Codec();
            }, true);
            AudioStream.RegisterCodec("opus", 4800, 2, () =>
            {

                return new OpusCodec(_opusEchoCanceller);
            }, true);
        }

        /// <summary>
        /// Initializes the native codecs
        /// </summary>
        private void InitJavaLibs()
        {
            if (!Build.CpuAbi.ToLower().Contains("x86") && !Build.CpuAbi.ToLower().Contains("arm64"))
            {
                Java.Lang.JavaSystem.LoadLibrary("audioprocessing");
                Java.Lang.JavaSystem.LoadLibrary("audioprocessingJNI");
            }
            Java.Lang.JavaSystem.LoadLibrary("opus");
            Java.Lang.JavaSystem.LoadLibrary("opusJNI");
            Java.Lang.JavaSystem.LoadLibrary("vpx");
            Java.Lang.JavaSystem.LoadLibrary("vpxJNI");
        }
        private void ReceiveNewPeer(string peerId)
        {
            if (_conference != null)
            {
                _conference.Link(peerId);
            }
        }
        private void ReceiveOfferAnswer(string peerId, string json)
        {
            if (_conference != null)
            {
                _conference.ReceiveOfferAnswer(JsonConvert.DeserializeObject<OfferAnswer>(json), peerId);
            }
        }
        private void ReceiveCandidate(string peerId, string json)
        {
            if (_conference != null)
            {
                _conference.ReceiveCandidate(JsonConvert.DeserializeObject<Candidate>(json), peerId);
            }
        }

        public async Task StartAsync(string roomName, ViewGroup videoContainer)
        {
            _roomName = roomName;
            await _signalingService.StartAsync();
            GetUserMedia(Application.Context, videoContainer);
            await _signalingService.JoinConferenceAsync(roomName);

        }
        public void SwitchCamera()
        {
            if (_localMedia == null)
                return;
            _localMedia.UseNextVideoDevice();
        }

        /// <summary>
        /// Toggles mute / unmute audio
        /// </summary>
        public void ToggleMute()
        {
            if (_localMedia == null)
                return;
            if (_localMedia.AudioIsMuted)
                _localMedia.UnmuteAudio();
            else
                _localMedia.MuteAudio();
        }

        /// <summary>
        /// Toggles mute / unmute video
        /// </summary>
        public void ToggleMuteVideo()
        {
            if (_localMedia == null)
                return;
            if (_localMedia.VideoIsMuted)
            {
                _localMedia.UnmuteVideo();
                _localMedia.UnmuteVideoPreview();
            }
            else
            {
                _localMedia.MuteVideo();
                _localMedia.MuteVideoPreview();
            }
        }

        public void GetUserMedia(Context context, ViewGroup videoContainer)
        {
            DefaultProviders.AndroidContext = context;

            UserMedia.GetMedia(new GetMediaArgs(true, true)
            {
                DefaultVideoPreviewScale = LayoutScale.Contain,
                DefaultVideoScale = LayoutScale.Contain,
                VideoWidth = 640,
                VideoHeight = 480,
                VideoFrameRate = 15,
                OnSuccess = (result) =>
                {
                    _localMedia = result.LocalStream;
                    _layoutManager = new AndroidLayoutManager(videoContainer);
                    _senderLocalVideoControl = (View)result.LocalVideoControl;
                    var localVideoStream = new VideoStream(result.LocalStream);
                    _conference = new Conference(STUN_TURN_URL, new Stream[]{ new AudioStream(result.LocalStream), localVideoStream });
                    _conference.RelayUsername = "test";
                    _conference.RelayPassword = "pa55word!";
                    _conference.DtlsCertificate = Certificate.GenerateCertificate();
                    _conference.OnLinkCandidate += Conference_OnLinkCandidate;
                    _conference.OnLinkOfferAnswer += Conference_OnLinkOfferAnswer;
                    
                    localVideoStream.OnLinkInit += LocalVideoStream_OnLinkInit;
                    localVideoStream.OnLinkDown += LocalVideoStream_OnLinkDown;
                    _layoutManager.SetLocalVideoControl(_senderLocalVideoControl);
                    _opusEchoCanceller = new OpusEchoCanceller(4800, 2);
                    _opusEchoCanceller.Start();
                }
            });

          
        }

        private void LocalVideoStream_OnLinkDown(StreamLinkDownArgs p)
        {
            if (_layoutManager != null)
                _layoutManager.RemoveRemoteVideoControl(p.PeerId);
        }

        private void LocalVideoStream_OnLinkInit(StreamLinkInitArgs p)
        {
            var videoControl = (View)p.Link.GetRemoteVideoControl();
            if (_layoutManager != null)
                _layoutManager.AddRemoteVideoControl(p.PeerId, videoControl);
        }

        private async void Conference_OnLinkOfferAnswer(LinkOfferAnswerArgs p)
        {
            await _signalingService.SendOfferAnswer(p.PeerId, JsonConvert.SerializeObject(p.OfferAnswer));
        }

        private async void Conference_OnLinkCandidate(LinkCandidateArgs p)
        {
            await _signalingService.SendICECandidate(p.PeerId, JsonConvert.SerializeObject(p.Candidate));
        }
    }
}