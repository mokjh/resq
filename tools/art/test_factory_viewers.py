import unittest
from types import SimpleNamespace
from generate_calibration import is_factory_viewer


class FactoryViewerSafetyTests(unittest.TestCase):
    def test_exact_empty_factory_buffers_only(self):
        for name,kind in [('Render Result','RENDER_RESULT'),('Viewer Node','COMPOSITING')]:
            self.assertTrue(is_factory_viewer(SimpleNamespace(name=name,type=kind,source='VIEWER',filepath='',packed_file=None)))

    def test_external_or_modified_buffers_are_never_discardable(self):
        original=dict(name='Render Result',type='RENDER_RESULT',source='VIEWER',filepath='',packed_file=None)
        for field,value in [('name','other'),('type','IMAGE'),('source','FILE'),('filepath','/external/image.png'),('packed_file',object())]:
            record=dict(original);record[field]=value
            self.assertFalse(is_factory_viewer(SimpleNamespace(**record)),field)


if __name__=='__main__': unittest.main()
