"""030-webrtc-comms-provider."""
from pipeline.game_edits import *  # noqa: F401,F403

STEPS = [
    # The game's UDP transport is replaced by the port's WebRTC one (unity/Port/Net), same interface.
    File("Assets/Scripts/Assembly-CSharp/UDPCommsProvider.cs", [
        Rename(r"\bUDPDataComms m_udpDataComms = new UDPDataComms\(\)",
               "PortNet.WebRtcDataComms m_udpDataComms = new PortNet.WebRtcDataComms()", count=1),
        Rename(r"(\tm_udpDataComms = new )UDPDataComms\(\)", r"\1PortNet.WebRtcDataComms()", count=1),
    ]),
]

RECIPE = Recipe("030-webrtc-comms-provider", STEPS)
