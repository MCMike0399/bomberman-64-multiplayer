import unittest
import json
from pathlib import Path
import tempfile

import builder_prepare as prepare


class SpendClosure(unittest.TestCase):
    def test_unclosed_previous_intent_blocks_another_paid_allocation(self):
        runs=[{'id':1},{'id':2},{'id':3}]
        def artifacts(identity):return[{'name':'builder-intent-1-1'}]if identity=='1'else[]
        self.assertEqual(prepare.unrecorded_intents(runs,{'recordedRuns':[]},3,artifacts),['1'])

    def test_current_run_and_recorded_cost_are_not_counted_twice(self):
        def artifacts(identity):raise AssertionError('Accounted/current runs must not be reopened')
        ledger={'recordedRuns':[{'workflowRunId':'1','estimateUsd':.2}]}
        self.assertEqual(prepare.unrecorded_intents([{'id':1},{'id':3}],ledger,3,artifacts),[])

    def test_conservative_full_lifetime_estimate_is_positive(self):
        value=prepare.estimate({'Linux':.526,'Windows':.710})
        self.assertGreater(value,3*(.526+.710));self.assertLess(value,8)

    def test_windows_infrastructure_only_rate_is_not_the_standard_licensed_rate(self):
        def product(operation,rate):return json.dumps({'product':{'attributes':{'operation':operation}},
            'terms':{'OnDemand':{'term':{'priceDimensions':{'hour':{'unit':'Hrs','pricePerUnit':{'USD':str(rate)}}}}}}})
        def aws(*args):
            filters=json.loads(args[args.index('--filters')+1]);system=next(row['Value']for row in filters if row['Field']=='operatingSystem')
            return {'PriceList':[product('RunInstances',.526)]if system=='Linux'else[
                product('RunInstances:0002:box',.526),product('RunInstances:0002',.710)]}
        with tempfile.TemporaryDirectory()as root:rates=prepare.price_rates(aws,Path(root))
        self.assertEqual(rates,{'Linux':.526,'Windows':.710})
