import copy
import unittest

from guard import violations


class GuardTests(unittest.TestCase):
    def setUp(self):
        self.before = {
            'containers': {'existing': {'health': 'healthy', 'restarts': 0, 'started': 'same'},
                           'blast-cluster-server-1': {'health': 'healthy', 'restarts': 0}},
            'units': {'chopperbot': {'ActiveState': 'active', 'MainPID': '10', 'NRestarts': '0'}},
            'http': {'pihole': {'status': 200, 'ms': 20}},
            'swap_used_mib': 700, 'available_mib': 5000, 'temperature': 58,
            'throttled': 0xe0000, 'dns': {'ok': True, 'ms': 0},
        }

    def changed(self, key, value):
        current = copy.deepcopy(self.before)
        current[key] = value
        return violations(self.before, current)

    def test_historical_throttling_does_not_abort(self):
        self.assertEqual([], violations(self.before, copy.deepcopy(self.before)))

    def test_current_throttling_aborts(self):
        self.assertIn('Pi temperature/throttling limit', self.changed('throttled', 0xe0001))

    def test_memory_reserve_and_swap_guard(self):
        self.assertTrue(self.changed('available_mib', 1000))
        self.assertTrue(self.changed('swap_used_mib', 1000))
        self.assertTrue(self.changed('pressure', 4))

    def test_dns_and_http_failure_abort(self):
        self.assertTrue(self.changed('dns', {'ok': False, 'ms': 0}))
        self.assertTrue(self.changed('http', {'pihole': {'status': 0, 'ms': 3000}}))

    def test_unhealthy_baseline_cannot_authorize_load(self):
        self.before['http']['pihole']['status'] = 503
        self.assertTrue(violations(self.before, copy.deepcopy(self.before)))

    def test_service_restart_aborts_even_if_active(self):
        self.assertTrue(self.changed('units', {'chopperbot': {'ActiveState': 'active', 'MainPID': '11', 'NRestarts': '1'}}))

    def test_existing_container_disappearance_aborts(self):
        self.assertTrue(self.changed('containers', {'blast-cluster-server-1': {'health': 'healthy', 'restarts': 0}}))

    def test_game_restart_aborts(self):
        current = copy.deepcopy(self.before)
        current['containers']['blast-cluster-server-1']['restarts'] = 1
        self.assertIn('game node unhealthy or restarted', violations(self.before, current))

    def test_pi_cpu_reserve_uses_counter_deltas(self):
        self.before['cpu_ticks'] = [100, 0, 50, 1000, 0, 0, 0, 0]
        self.assertTrue(self.changed('cpu_ticks', [1000, 0, 50, 1010, 0, 0, 0, 0]))
        self.assertFalse(self.changed('cpu_ticks', [200, 0, 50, 1400, 0, 0, 0, 0]))


if __name__ == '__main__':
    unittest.main()
