"""Entry point of the packaged builder app (PyInstaller runs this file)."""
import sys

from builder.__main__ import main

sys.exit(main())
