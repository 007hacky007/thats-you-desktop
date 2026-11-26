"""Unity 6 API renames, as Unity's script API updater makes them (shared by 004 and 014)."""
from pipeline.game_edits import Rename

# (pattern, replacement): member renames between Unity 5.3 and Unity 6
RULES = [
    (r"\.FindChild\(", ".Find("),
    (r"\.hdr\b", ".allowHDR"),
    (r"\bComputeBufferType\.DrawIndirect\b", "ComputeBufferType.IndirectArguments"),
    (r"\bGraphics\.DrawProceduralIndirect\(", "Graphics.DrawProceduralIndirectNow("),
    (r"(\b[Rr]igidbody|\bconnectedBody)\.velocity\b", r"\1.linearVelocity"),
    (r"(\b[Rr]igidbody|\bconnectedBody)\.drag\b", r"\1.linearDamping"),
    (r"(\b[Rr]igidbody|\bconnectedBody)\.angularDrag\b", r"\1.angularDamping"),
    (r"\.Resize\(", ".Reinitialize("),
]


def ops():
    return [Rename(p, r, optional=True) for p, r in RULES]
