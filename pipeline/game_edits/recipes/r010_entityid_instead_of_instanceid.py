"""010-entityid-instead-of-instanceid."""
from pipeline.game_edits import Delete, File, Lines, Recipe  # noqa: F401

STEPS = [
    File("Assets/Plugins/Assembly-CSharp-firstpass/UnityStandardAssets/Water/Water.cs", [
        Lines(253, 1, "5a4ebe476c6ed33e", '\t\t\t\tGameObject gameObject2 = new GameObject("Water Refr Camera id" + GetEntityId() + " for " + currentCamera.GetEntityId(), typeof(Camera), typeof(Skybox));\n'),
        Lines(245, 1, "c6fc237142bf9506", '\t\t\t\tm_RefractionTexture.name = "__WaterRefraction" + GetEntityId();\n'),
        Lines(224, 1, "f4a32611b41ff6e7", '\t\t\t\t\tGameObject gameObject = new GameObject("Water Refl Camera id" + GetEntityId() + " for " + currentCamera.GetEntityId(), typeof(Camera), typeof(Skybox));\n'),
        Lines(216, 1, "076dc2902484a4ac", '\t\t\t\t\tm_ReflectionTexture.name = "__WaterReflection" + GetEntityId();\n'),
    ]),
    File("Assets/Scripts/Assembly-CSharp/AkAutoObject.cs", [
        Lines(9, 1, "3d2d0c6ebe169597", '\t\tm_id = AkSoundEngine.AkObjectId(GameObj);\n'),
    ]),
    File("Assets/Scripts/Assembly-CSharp/AkSoundEngine.cs", [
        Lines(784, 1, "44bb96bda81239ca", '\t\t\tresult = AkObjectId(in_gameObject);\n'),
        Lines(774, 1, "0e116e9cefd34ab2", '\t\t\tRegisteredGameObjects.Remove(AkObjectId(in_gameObjectID));\n'),
        Lines(771, 1, "63a4cf1955d2dcd7", '\t\tAKRESULT aKRESULT = UnregisterGameObjInternal(AkObjectId(in_gameObjectID));\n'),
        Lines(760, 1, "1ab6dec11a2534bc", '\t\t\tRegisteredGameObjects.Add(AkObjectId(in_gameObjectID));\n'),
        Lines(757, 1, "d91c2027bb55cae6", '\t\tAKRESULT aKRESULT = RegisterGameObjInternal_WithName_WithMask(AkObjectId(in_gameObjectID), in_gameObjectID.name, in_uListenerMask);\n'),
        Lines(746, 1, "1ab6dec11a2534bc", '\t\t\tRegisteredGameObjects.Add(AkObjectId(in_gameObjectID));\n'),
        Lines(743, 1, "b0a1f7cbdc17fe8f", '\t\tAKRESULT aKRESULT = RegisterGameObjInternal_WithName(AkObjectId(in_gameObjectID), in_gameObjectID.name);\n'),
        Lines(732, 1, "1ab6dec11a2534bc", '\t\t\tRegisteredGameObjects.Add(AkObjectId(in_gameObjectID));\n'),
        Lines(729, 1, "4e2697cfa972d903", '\t\tAKRESULT aKRESULT = RegisterGameObjInternal_WithMask(AkObjectId(in_gameObjectID), in_uListenerMask);\n'),
        Lines(718, 1, "1ab6dec11a2534bc", '\t\t\tRegisteredGameObjects.Add(AkObjectId(in_gameObjectID));\n'),
        Lines(715, 1, "b304af95c1f96aaa", '\t\tAKRESULT aKRESULT = RegisterGameObjInternal(AkObjectId(in_gameObjectID));\n'),
        Lines(706, 1, "b027716c8f4b9f29", '\t\treturn RegisteredGameObjects.Contains(AkObjectId(in_gameObject));\n'),
        Lines(704, 0, "e3b0c44298fc1c14", (
            '\t// Port: Unity 6.6 made Object.GetInstanceID() an obsolete-as-error API in favour of\n'
            '\t// GetEntityId(). The Wwise C# layer and its native P/Invoke ABI identify game objects by\n'
            '\t// an int (RegisterGameObjInternal*, RegisteredGameObjects, AkAutoObject.m_id), so every\n'
            '\t// call site derives the int through this one helper. EntityId.GetHashCode() is stable\n'
            '\t// and unique per live object (it is the wrapped id), so register/unregister/lookup keep\n'
            '\t// matching exactly as they did with GetInstanceID(). The Wwise backend is replaced later.\n'
            '\tinternal static int AkObjectId(GameObject go)\n'
            '\t{\n'
            '\t\treturn go.GetEntityId().GetHashCode();\n'
            '\t}\n'
            '\n'
        )),
    ]),
    File("Assets/Scripts/Assembly-CSharp/touchBendingCollisionGS.cs", [
        Lines(138, 1, "9467151d7ff93a59", '\t\t\tif (other.GetEntityId() == Player_ID)\n'),
        Lines(118, 1, "045fcaf4611bdee8", '\t\tPlayer1_ID = other.GetEntityId();\n'),
        Lines(96, 1, "fef01317c0fdc139", '\t\t\tPlayer_ID = other.GetEntityId();\n'),
        Lines(62, 1, "01a8cc142af0d33b", '\tprivate EntityId Player1_ID = EntityId.None;\n'),
        Lines(54, 1, "3ba65214e2b3a558", '\tprivate EntityId Player_ID = EntityId.None;\n'),
    ]),
]

RECIPE = Recipe("010-entityid-instead-of-instanceid", STEPS)
