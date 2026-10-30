import unittest

import builder_job


class ActualJobClock(unittest.TestCase):
    def test_selects_this_live_runner_not_an_earlier_completed_job(self):
        job={'id':2,'status':'in_progress','runner_name':'actual','started_at':'2026-10-30T00:00:00Z'}
        self.assertEqual(builder_job.select([{'id':1,'status':'completed'},job],'actual'),job)

    def test_ambiguous_jobs_never_invent_a_start_time(self):
        with self.assertRaises(ValueError):builder_job.select([{'id':1,'status':'in_progress'},{'id':2,'status':'in_progress'}],'unknown')
