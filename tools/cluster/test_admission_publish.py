import unittest
from admission_publish import placement


class PublicationTests(unittest.TestCase):
    def test_maps_only_the_pressured_nodes_own_servers(self):
        state={'at':100,'admission':{'one':{'overloaded':True},'two':{'overloaded':False}},'nodes':[
            {'name':'one','fleet':{'nodeId':'a','value':[{'nodeId':'a','serverId':'primary'},{'nodeId':'b','serverId':'fallback'}]}},
            {'name':'two','fleet':{'nodeId':'b','value':[{'nodeId':'a','serverId':'primary'},{'nodeId':'b','serverId':'fallback'}]}}]}
        self.assertEqual(placement(state,100),{'at':100,'excludedServers':['primary']})
        with self.assertRaises(ValueError): placement(state,161)
        with self.assertRaises(ValueError): placement(state,94)


if __name__=='__main__': unittest.main()
