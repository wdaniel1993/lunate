import unittest

from report import average


class AverageTests(unittest.TestCase):
    def test_average_of_three_values(self):
        self.assertEqual(2.0, average([1, 2, 3]))

    def test_average_of_one_value(self):
        self.assertEqual(5.0, average([5]))


if __name__ == "__main__":
    unittest.main()
