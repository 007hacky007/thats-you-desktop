package console

import (
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

func repoCatalogue(t *testing.T) *Catalogue {
	t.Helper()
	_, file, _, _ := runtime.Caller(0)
	p := filepath.Join(filepath.Dir(file), "..", "..", "..", "protocol", "messages.json")
	c, err := LoadCatalogue(p)
	if err != nil {
		t.Fatalf("load catalogue: %v", err)
	}
	return c
}

// Every message of contract 5.5 and protocol.md section 4.
var requiredMessages = map[string]string{
	"ClientRequestPlayerIDMessage": "p2g", "DeviceInfoMessage": "p2g", "ClientPlayerProfileMessage": "p2g",
	"ClientDisconnectMessage": "p2g", "AnswerIndexResponseMessage": "p2g", "JokerConsumedResponseMessage": "p2g",
	"ContinuePressedResponseMessage": "p2g", "UsePicPressedResponseMessage": "p2g", "TextAnswerReponseMessage": "p2g",
	"StartGameButtonPressedResponseMessage": "p2g", "TopDrawerPhotoTakenResponseMessage": "p2g",
	"TrophyPhotoFXInfoMessage": "p2g", "TrophyDrawingColouringInfoMessage": "p2g",
	"ClientImageResourceContentTransferMessage": "p2g", "RequestResourceMessage": "p2g",
	"AllResourcesReceivedMessage": "p2g", "ClientSendChatLogMessage": "p2g",
	"ClientImageUGCContentMessage": "p2g", "ClientQuestionUGCContentMessage": "p2g",
	"ClientAudioClipContentTransferMessage": "p2g", "CancelContentSendMessage": "p2g",
	"AcknowledgeContentTransferMessage": "both",
	"WishResponseMessage":               "p2g", "JoinResponseMessage": "p2g", "PlayerNameResponseMessage": "p2g",
	"PlayerCardResponseMessage": "p2g", "UGCDeclinedMessage": "p2g",
	"InterfaceVersionMessage": "g2p", "SessionStateMessage": "g2p", "AssignPlayerIDAndSlotMessage": "g2p",
	"DisconnectCommandMessage": "g2p", "ClientOnlineStateMessage": "g2p", "ClientUGCStateMessage": "g2p",
	"JoinLeaveMessage": "g2p", "PlayerJoinedMessage": "g2p", "PlayerNameQuizStateMessage": "g2p",
	"PlayerLeftMessage": "g2p", "RejoiningClientOwnProfileMessage": "g2p", "ClientGameIDMessage": "g2p",
	"SyncJokersQuizStateMessage": "g2p", "ClientReceiveChatLogMessage": "g2p", "QuestionDataMessage": "g2p",
	"ResourceRequirementsMessage": "g2p", "ImageResourceContentTransferMessage": "g2p",
	"PortraitThumbnailContentTransferMessage": "g2p", "AnswerTextQuizStateMessage": "g2p",
	"WouldYouRatherAnswerTextQuizMessage": "g2p", "ClientQuizCommandMessage": "g2p",
	"ClientHoldingScreenCommandMessage": "g2p", "ClientQuickDrawHoldingScreenCommandMessage": "g2p",
	"ClientTextEntryCommandMessage": "g2p", "ClientAnswerCommandMessage": "g2p",
	"ClientStartDrawingCommandMessage": "g2p", "ClientTopDrawerTakePhotoCommandMessage": "g2p",
	"ClientMimicThisCommandMessage": "g2p", "ClientQuickDrawTakePhotoCommandMessage": "g2p",
	"ClientScenarioTransitionCommandMessage": "g2p", "ClientJokerCommandMessage": "g2p",
	"ClientSelectedUGCRequestMessage": "g2p", "ClientEndOfGameMessage": "g2p", "WishCommandMessage": "g2p",
}

func TestCatalogueComplete(t *testing.T) {
	c := repoCatalogue(t)
	for name, dir := range requiredMessages {
		m, ok := c.Message(name)
		if !ok {
			t.Errorf("missing message %s", name)
			continue
		}
		if m.Dir != dir {
			t.Errorf("%s: dir %s, want %s", name, m.Dir, dir)
		}
	}
	for _, name := range c.MessageNames() {
		if _, ok := requiredMessages[name]; !ok {
			t.Errorf("unexpected message %s (add it to the list)", name)
		}
	}
	if c.InterfaceVersion != "2016-07-12-1645" || c.JSONTransferID != -7503 {
		t.Errorf("constants: %q %d", c.InterfaceVersion, c.JSONTransferID)
	}
	for _, e := range []string{"eAction", "eHoldingScreenType", "RoundType", "eDisconnectDisplayMessage", "ResponseType", "ImageType", "Requirement"} {
		if len(c.Enums[e]) == 0 {
			t.Errorf("missing enum %s", e)
		}
	}
	if c.Enums["eAction"]["EditProfile"] != 31 || c.Enums["RoundType"]["WouldYouRather"] != 128 {
		t.Errorf("enum values wrong")
	}
	for _, ty := range []string{"UGCInfo", "ResourceRequirement"} {
		if len(c.Types[ty]) == 0 {
			t.Errorf("missing type %s", ty)
		}
	}
}

func TestCatalogueInheritance(t *testing.T) {
	c := repoCatalogue(t)
	m, _ := c.Message("ClientQuickDrawHoldingScreenCommandMessage")
	var names []string
	for _, f := range m.Fields {
		names = append(names, f.Name)
	}
	got := strings.Join(names, ",")
	want := "action,time,HoldingScreenText,HoldingScreenType,OtherPlayerIndex,ShowPortraitPhotoControls,ImageGUID,Heading,Description,NumberOfDrawingsRemaining,PlayerName"
	if got != want {
		t.Errorf("fields %s", got)
	}
}

func mustDecode(t *testing.T, s string) any {
	t.Helper()
	v, err := Decode([]byte(s))
	if err != nil {
		t.Fatal(err)
	}
	return v
}

func TestValidate(t *testing.T) {
	c := repoCatalogue(t)
	cases := []struct {
		dir, js string
		ok      bool
		frag    string
	}{
		{"p2g", `{"TypeString":"ClientRequestPlayerIDMessage","UID":"abc"}`, true, ""},
		{"p2g", `{"TypeString":"ClientRequestPlayerIDMessage","UID":""}`, false, "rule failed"},
		{"p2g", `{"TypeString":"ClientRequestPlayerIDMessage"}`, false, "missing field UID"},
		{"p2g", `{"TypeString":"ClientRequestPlayerIDMessage","UID":5}`, false, "want string"},
		{"p2g", `{"TypeString":"Nope"}`, false, "unknown TypeString"},
		{"p2g", `{"UID":"x"}`, false, "missing TypeString"},
		{"p2g", `[1]`, false, "not a JSON object"},
		{"g2p", `{"TypeString":"ClientRequestPlayerIDMessage","UID":"abc"}`, false, "travels p2g"},
		{"p2g", `{"TypeString":"AnswerIndexResponseMessage","AnswerIndex":1.5}`, false, "want int"},
		{"p2g", `{"TypeString":"AnswerIndexResponseMessage","AnswerIndex":-3}`, false, "rule failed"},
		{"p2g", `{"TypeString":"AllResourcesReceivedMessage","Requirements":[{"GUID":"g","Rqrmnt":1}]}`, true, ""},
		{"p2g", `{"TypeString":"AllResourcesReceivedMessage","Requirements":[{"GUID":"g"}]}`, false, "missing field Rqrmnt"},
		{"p2g", `{"TypeString":"ClientPlayerProfileMessage","playerName":"Al","uppercasePlayerName":"AL","deviceCultureName":"en-US","ugcInfo":{"scenarioInt":-1,"scenario":-100,"challengeID":"","creationID":"","set":false},"isGuest":0,"playerCardId":"CARD_3"}`, true, ""},
		{"p2g", `{"TypeString":"ClientPlayerProfileMessage","playerName":"Al","uppercasePlayerName":"AL","deviceCultureName":"en-US","ugcInfo":{"scenarioInt":-1,"scenario":-100,"challengeID":"","creationID":"","set":false},"isGuest":2,"playerCardId":"CARD_3"}`, false, "isGuest"},
		{"p2g", `{"TypeString":"ClientPlayerProfileMessage","playerName":"Al","uppercasePlayerName":"AL","deviceCultureName":"en-US","ugcInfo":{"scenarioInt":-1},"isGuest":0,"playerCardId":"CARD_3"}`, false, "UGCInfo: missing field"},
		{"g2p", `{"TypeString":"ClientHoldingScreenCommandMessage","action":5,"time":0.0,"HoldingScreenText":"Bob","HoldingScreenType":3,"OtherPlayerIndex":0,"ShowPortraitPhotoControls":false}`, false, "OtherPlayerIndex"},
		{"g2p", `{"TypeString":"ClientHoldingScreenCommandMessage","action":5,"time":0.0,"HoldingScreenText":"","HoldingScreenType":4,"OtherPlayerIndex":-1,"ShowPortraitPhotoControls":false}`, true, ""},
		{"g2p", `{"TypeString":"ClientQuickDrawHoldingScreenCommandMessage","action":5,"time":0.0,"HoldingScreenText":"***NOTSETHOLDINGSCREENTEXT","HoldingScreenType":5,"OtherPlayerIndex":-1,"ShowPortraitPhotoControls":false,"ImageGUID":"g","Heading":"h","Description":"d","NumberOfDrawingsRemaining":2,"PlayerName":"p"}`, true, ""},
		{"g2p", `{"TypeString":"ClientQuizCommandMessage","action":-1,"time":0.0}`, false, "action"},
		{"g2p", `{"TypeString":"ClientQuizCommandMessage","action":31,"time":0}`, true, ""},
		{"p2g", `{"TypeString":"JoinResponseMessage"}`, false, "not used by the port"},
	}
	for _, tc := range cases {
		_, errs := c.Validate(tc.dir, mustDecode(t, tc.js))
		if tc.ok && len(errs) > 0 {
			t.Errorf("%s: unexpected errors %v", tc.js, errs)
		}
		if !tc.ok {
			if len(errs) == 0 {
				t.Errorf("%s: expected error", tc.js)
			} else if !strings.Contains(strings.Join(errs, "|"), tc.frag) {
				t.Errorf("%s: errors %v lack %q", tc.js, errs, tc.frag)
			}
		}
	}
}

func TestBuildFillsDefaultsInOrder(t *testing.T) {
	c := repoCatalogue(t)
	o, err := c.Build("ClientHoldingScreenCommandMessage", map[string]any{"HoldingScreenType": 4, "action": 5, "HoldingScreenText": ""}, true)
	if err != nil {
		t.Fatal(err)
	}
	got := string(Marshal(o))
	want := `{"TypeString":"ClientHoldingScreenCommandMessage","action":5,"time":0.0,"HoldingScreenText":"","HoldingScreenType":4,"OtherPlayerIndex":-1,"ShowPortraitPhotoControls":false}`
	if got != want {
		t.Errorf("got %s", got)
	}
	o, _ = c.Build("ClientPlayerProfileMessage", nil, true)
	if _, errs := c.Validate("p2g", o); len(errs) > 0 {
		t.Errorf("defaults invalid: %v", errs)
	}
}

func TestCatalogueRejectsBadFile(t *testing.T) {
	bad := []string{
		`{"schema":2,"messages":{}}`,
		`{"schema":1,"messages":{"A":{"dir":"x","fields":[]}}}`,
		`{"schema":1,"messages":{"A":{"dir":"p2g","fields":[{"name":"f","type":"weird"}]}}}`,
		`{"schema":1,"messages":{"A":{"dir":"p2g","base":"B","fields":[]}}}`,
		`{"schema":1,"messages":{"A":{"dir":"p2g","fields":[],"valid":[{"field":"x","op":"eq","value":1}]}}}`,
	}
	for _, b := range bad {
		if _, err := ParseCatalogue([]byte(b)); err == nil {
			t.Errorf("accepted %s", b)
		}
	}
}
