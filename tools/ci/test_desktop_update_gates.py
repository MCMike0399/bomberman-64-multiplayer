"""No helper-only, stale or partial updater receipt can authorize a player release."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

import desktop_packages as desktop
import desktop_update_gates as gates
from test_desktop_packages import write_desktop, SHA, RELEASE


class DesktopUpdateGateTests(unittest.TestCase):
    def test_actual_candidate_install_rollback_and_rendering_evidence_is_required(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            write_desktop(root)
            target = 'windows-x64'
            item = desktop.names(target)
            receipt = json.loads((root / item['updater_test']).read_text())
            archive = desktop.digest(root / item['archive'])
            gates.inspect(receipt, target, SHA, RELEASE, archive)
            mutations = [('passed', False), ('cases', []), ('candidateArchiveSha256', '0' * 64)]
            for field, value in mutations:
                with self.subTest(field=field), self.assertRaises(RuntimeError):
                    gates.inspect(dict(receipt, **{field: value}), target, SHA, RELEASE, archive)
            for field, value in [('oldGameplay', False), ('inventoryMatched', False), ('uninstallVerified', False),
                                 ('health', {}), ('seconds', float('nan'))]:
                wrong = copy.deepcopy(receipt)
                wrong['cases'][0][field] = value
                with self.subTest(field=field), self.assertRaises(RuntimeError):
                    gates.inspect(wrong, target, SHA, RELEASE, archive)


if __name__ == '__main__':
    unittest.main()
