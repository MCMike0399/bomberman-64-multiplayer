import json
import tempfile
import unittest
from pathlib import Path
from metal_metrics import read_batches, summarize


class MetalMetricsTests(unittest.TestCase):
    def write(self, directory, events):
        path=Path(directory)/'metal.ndjson'
        path.write_text('Filtering the log data\n'+'\n'.join(json.dumps(e) for e in events)+'\n')
        return path

    def batch(self, pid=12, time='2026-10-24 12:00:02.000000-0600', gpu=2):
        pairs=','.join(f'10,{gpu}' for _ in range(100))
        return dict(processID=pid,timestamp=time,eventMessage='metal-HUD: 300,128,256,'+pairs)

    def test_parse_real_header_shape_and_filter_other_processes(self):
        with tempfile.TemporaryDirectory() as d:
            path=self.write(d,[self.batch(pid=99),self.batch()])
            batches=read_batches(path,12)
            self.assertEqual(1,len(batches));self.assertEqual(300,batches[0]['firstFrame'])
            self.assertEqual([2]*100,batches[0]['gpuMs'])
            self.assertEqual(1,batches[0]['end']-batches[0]['start'])

    def test_alignment_discards_batches_crossing_sample_boundaries(self):
        with tempfile.TemporaryDirectory() as d:
            early=self.batch(time='2026-10-24T18:00:01Z',gpu=99)
            early['eventMessage']=early['eventMessage'].replace('300,128,256','200,128,256')
            path=self.write(d,[early,self.batch()])
            window=dict(scene='menu',preset='High',startUtc='2026-10-24T18:00:00.5Z',endUtc='2026-10-24T18:00:03Z')
            result=summarize(path,12,dict(measurements=[window]))
            self.assertEqual(100,result['measurements'][0]['samples'])
            self.assertEqual(2,result['measurements'][0]['gpuP95Ms'])

    def test_missing_zero_and_nonfinite_gpu_data_cannot_pass(self):
        with tempfile.TemporaryDirectory() as d:
            for rows in [[],[self.batch(gpu=0)],[self.batch(gpu='nan')]]:
                with self.assertRaises(ValueError):summarize(self.write(d,rows),12,dict(measurements=[]))

    def test_incomplete_batch_cannot_be_misread_as_pairs(self):
        with tempfile.TemporaryDirectory() as d:
            event=self.batch();event['eventMessage']='metal-HUD: 1,128,256,10,2,10'
            with self.assertRaises(ValueError):read_batches(self.write(d,[event]),12)

    def test_duplicate_system_records_do_not_double_count_gpu_samples(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertEqual(1,len(read_batches(self.write(d,[self.batch(),self.batch()]),12)))
            with self.assertRaises(ValueError):read_batches(self.write(d,[self.batch(),self.batch(gpu=3)]),12)

    def test_console_payload_joins_redacted_log_only_by_pid_frame_and_header(self):
        with tempfile.TemporaryDirectory() as d:
            full=self.batch();redacted=full | dict(eventMessage='metal-HUD: 300,128,256<private>')
            path=self.write(d,[redacted]);console=Path(d)/'runtime.log'
            console.write_text('2026-10-24 12:00:02.000 Game[12:999] '+full['eventMessage']+'\n')
            self.assertEqual([2]*100,read_batches(path,12,console)[0]['gpuMs'])
            for text in [console.read_text().replace('[12:', '[99:'),console.read_text().replace('300,128,256','300,128,257')]:
                console.write_text(text)
                with self.assertRaises(ValueError):read_batches(path,12,console)


if __name__=='__main__':unittest.main()
