"""Entry point of the packaged builder app (PyInstaller runs this file)."""
import multiprocessing
import sys

if __name__ == "__main__":
    # The audio step runs spawn worker processes; in the packaged app each of them starts this
    # executable again, and freeze_support() turns those starts into workers instead of builders.
    multiprocessing.freeze_support()
    from builder.__main__ import main

    sys.exit(main())
