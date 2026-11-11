"""SM5 instruction semantics, independently of the material implementation.

https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/swapc--sm5---asm-
"""
import unittest
from sm5_reference import execute


class SwapcTests(unittest.TestCase):
    def test_nonzero_condition_exchanges_sources_per_lane(self):
        result = execute('swapc o0, o1, r0, r1, r2', {
            'r0': [0, 1, -1, 0], 'r1': [1, 2, 3, 4], 'r2': [5, 6, 7, 8]}, {})
        self.assertEqual(result['o0'], [1, 6, 7, 4])
        self.assertEqual(result['o1'], [5, 2, 3, 8])

    def test_sources_are_read_before_either_destination_is_written(self):
        result = execute('swapc o0.xy, o1.xy, r0, o0, o1', {
            'r0': [1, 0, 1, 0], 'o0': [1, 2, 3, 4], 'o1': [5, 6, 7, 8]}, {})
        self.assertEqual(result['o0'], [5, 2, 3, 4])
        self.assertEqual(result['o1'], [1, 6, 7, 8])


if __name__ == '__main__':
    unittest.main()
