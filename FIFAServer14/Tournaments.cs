namespace FIFAServer14;

internal static class Tournaments
{
    private static readonly Dictionary<int, int[]> TeamPools = new()
    {
        [ 1] = new[] { 422, 1572, 357, 294, 922, 1914, 1744, 873, 697, 689, 1939, 110, 696, 12, 15005 },
        [ 2] = new[] { 1926, 162, 2023, 433, 94, 298, 165, 1910, 256, 1871, 570, 97, 3, 8, 1880 },
        [ 3] = new[] { 1887, 1902, 62, 1807, 1915, 417, 614, 191, 91, 665, 200, 459, 95, 14, 1884 },
        [ 4] = new[] { 57, 378, 605, 2007, 1888, 472, 190, 674, 1913, 10020, 226, 1882, 29, 1795, 71 },
        [ 5] = new[] { 896, 1906, 1903, 31, 673, 1881, 1808, 58, 1844, 379, 242, 1838, 1901, 4, 1876 },
        [ 6] = new[] { 203, 1861, 1799, 1793, 78, 171, 453, 1893, 1837, 1909, 10029, 1878, 232, 1908, 15029 },
        [ 7] = new[] { 229, 1961, 217, 744, 1598, 1952, 231, 246, 1892, 468, 244, 189, 1032, 192, 166 },
        [ 8] = new[] { 72, 479, 1917, 206, 1970, 1879, 169, 1039, 819, 1853, 1843, 25, 1891, 483, 54 },
        [ 9] = new[] { 2, 38, 1013, 1719, 109, 450, 485, 1792, 70, 106, 59, 1824, 1809, 28, 1877 },
        [10] = new[] { 19, 247, 1028, 15, 1806, 66, 569, 452, 23, 312, 32, 36, 383, 1842, 245 },
        [11] = new[] { 17, 39, 1819, 393, 65, 462, 517, 315, 480, 567, 1053, 219, 1860, 50, 9 },
        [12] = new[] { 1960, 568, 280, 74, 1629, 598, 237, 1043, 69, 175, 449, 448, 573, 1, 481 },
        [13] = new[] { 13, 7, 144, 1896, 34, 1048, 55, 1035, 1041, 461, 22, 18, 457, 44, 48 },
        [14] = new[] { 52, 234, 325, 236, 47, 46, 10, 5, 11, 21, 73, 240, 45, 243, 241 },
    };

    private static readonly int[] DefaultTeamIds =
        { 52, 234, 325, 236, 47, 46, 10, 5, 11, 21, 73, 240, 45, 243, 241 };

    internal static int[] GetTeamIds(int tournamentId) =>
        TeamPools.TryGetValue(tournamentId, out var pool) ? pool : DefaultTeamIds;

    internal static readonly (int Id, string Name, int Design, int Diff, int Coins, int Unlock)[] Defs =
    {
        ( 1, "Starter Cup",                1100, 1,  300,  0),  // Amateur,      first win 500 + Club Customisation Pack
        ( 2, "Midlands Invitational",      1104, 2,  500,  0),  // Semi-Pro,     first win 600
        ( 3, "Gold Challenge",             1108, 3,  700,  0),  // Professional, first win 1000
        ( 4, "Quad-League Classic",        1112, 2,  600,  1),  // Semi-Pro,     first win 700
        ( 5, "Managers Cup",               1116, 3,  700,  1),  // Professional, first win 700 + Silver Gift Pack
        ( 6, "Bronze International Shield",1120, 4, 1000,  2),  // World Class,  first win 1000 + Gold Gift Pack
        ( 7, "Trio Showcase",              1124, 2,  300,  2),  // Semi-Pro,     first win Silver Contracts Pack
        ( 8, "Unified Cup",                1128, 3, 1000,  2),  // Professional, first win 1250
        ( 9, "Pyramid Invitational",       1132, 4, 1000,  3),  // World Class,  first win 1000 + Mixed Contracts Pack
        (10, "Silver Links Cup",           1136, 3,  700,  4),  // Professional, first win 1000
        (11, "Federation Cup",             1140, 4,  200,  4),  // World Class,  first win 2000
        (12, "Champions Trophy",           1144, 5, 2500,  4),  // Legendary,    first win 2500
        (13, "Premier Clash",              1148, 3, 1200,  5),  // Professional, first win 1500
        (14, "Ultimate Cup",               1152, 3, 3000, 10),  // Professional, first win 3000 + Gold Pack
    };

    // [1] = Amateur (Danish+Greek) for Tournaments 1-3
    // [4] = Semi-Pro (Scandinavian) for Tournaments 4-5
    // [6] = Pro (Europa League) for Tournaments 6-9
    // [10]= Legendary (Champions League elite) for Tournaments 10-14
    private static readonly Dictionary<int, string> BracketBlobs = new()
    {
        [ 1] = "7goAAB+LCAALmW9qAv/t1UtPE1EUB/Bz2tIHFMr7/ShQni0manwrooAaRTGVLyCmMV04JkRd+DW6I9ElOxfu5Fuw1uDCrYlfwAXe/517x9OZs3fjSSa3/fHv3GHumTtMYZ2eVrJHHH6uZMOx4b4vuvEwHY7vc+F44nJN58epcPzsxpbLBW6OL86L7vuH6+48v5nYzfG//l31msMvA9Y05caMcxwf3QhLCd+s79Vp7/Xbg+D5q0bwprwdvGwGjfKzxsG75ouGC3/d2o/W283jWoJcC9nzyp5yLUaulSjvxgK195zvRd9b3W7scWMp+i/ZndlcwxjVd5/W64RP9gnYyPbS2VmfOcI/O+w3MABMCRw0MARMCxw2MALMCBw1MAbsEDhuYAKYFThpYAqYEzhtYAaYt/jTYtnALLAgcM7APLBTYMXAArBL4KKBJWDR4vp34LKBFWC3wFUDVWCPxR2LNQNrQHvrmHGh9M2sEU/QUexJZiC2FOkpIPaXlMA0EJtNWmAGeJj+2xmoDiC2oaaIZoHog6xI5oBI5QTmgeicvMACEFtXQWAnEPvYsbjQLmAgmi5sPIPY4Yri593AomjHsCUNYu/rEViyF28ejFLbA8lTFmX1ATGR5H7i6baZUQNA3DqZHCSeabttqCHgSWyNhonL0V7uawSIhZPJUeLZ6C3hawyINZI+TjwX7fu+JoCtWHKSeD7RS1PAeC9NE1eid4yvGeBx7C6VidH4tm+jmvUok3PES4nkvEeZrBAvJ5ILHmVykXglkVzyKJPLxNVEcsWjTK4S1xLJqkeZrBGfSyTXPMpkoL0bWjkF481h67z28wsaXtQw3hy2LmnJyxpe0fCqhtc01CanGxre1PCWhusa3tZwQ8M7Gt7VcFPDLQ23Nbyn4X0NH2h4mFbwoZZ8pOGOho81fKLhroaf9lu/Nn4w+YP+AJ94qRfuCgAA",
        [ 2] = "7goAAB+LCAALmW9qAv/t1clu01AUBuBznDRDp3Se0iFTp7RFAsRUoBTaAoJCUeiiq0gpilAXBKmCPgIvwJ5FpS5Z8CasQWXBpgskXoBFub99r3tinz0bjmTd+Mvv2PE9vmYK6vT0mD6kg8/H1s7s/hcOxrr1qt0/sftHNkfWd+z+qheMDft1wo4ZO+7Z3Nc/TGyP/V//rvouptCfU8+OSevYPtkR5gnfqO3WaPft+8NW402z9a6w1Xp90GoWXjYPjw5eNW342+Z+ON/2PF6kN5KRnkrZ/XSkd7LU3nOuF7vt2GPHXjvmwn/JCXcgj1Nt50WtRvjkPwHrqT46P+83W/C1xQEDg0BP4JCBYWBC4IiBUWBS4JiBcWCHwAkDeWBK4KSBKWBa4LSBGWDGx18+FgwUgVmBJQNlYKfAioFZYJfAOQPzwG4f134AFwwsAnsEVg0sAXt93PZx2cAK0L91zLhQ+m7miPPhKnIxQQaPRWsF027wLH0x/0EPGMRikxCYBNZFZ6A6gFiGjsTJUsAT0TNB3xhESl5VBojryQjMArF0ZQV2Ale99qvvAjZE0wWNZzAhOjDoQoMZ0Y5BSxrE2tcrMAfEg5FreyB5ykdZ/cBE5H4OEE+3/RvUILAeSQ4Rz7TdNtQw8CSSHCEuhGu5q1EgJk4mx4iL4VvC1TgQcyR9grgUrvuu8kDcZJmcJC7HemkKGO2laeJK+I5xNQOkyNkLxGh8v2/DKjqUyRLxfCxZdiiTFeKFWHLWoUzOES/GkvMOZXKBeCmWXHQok1Xi5VhyyaFMLhNfiiVXHMpkQ3s3rHoKRpvDr8va4Vc0vKrhnvab17TkdQ1vaHhTw1vq39TwtoZ3NLyr4ZqG9zRc1/C+hg803NBwU8MtDR9q+EjDxxrWNXyi4VMNtzV8puFzDXc0/Lz/8ff6Tya30V/5O4ZF7goAAA==",
        [ 3] = "7goAAB+LCAALmW9qAv/t1UtPE1EUB/Bz29LybHlDKYW+eIOJGt9aUUCNopjKriZETDEsrAlRN3wMV25M/AB+hm6bsHGtwYVbE7+AC7z/mXMnpzNn78aTTG7n15N53f/cMeTX6WkztZfyfzd5rPJ/Gd4/5vGz8ceDmD+2uK/O40f2Nu+fcP8e76d5fM7H+/rHkOGe//XvatBubhowpzEeE+zYPvGY4P+db9R2a7T75t1R88XrRvNtYav56rDZKDxrHL0/fNng5m+b+8F883k4KhTnMUGdmUryyFGhbh57QplzWezn/YFQ1jLBXRo+lb2GLNV2ntZqhF/eG7CeHKSzsyG7+X8zDlsYAcYEjloYA8YFjluYACYETlrIArsETlnIAZMCpy3kgSmBMxZmgd0e/vKwYKEI7BFYslAG9gqsWJgD9gmct7AA7Pew+gO4aGEJOCBw2cIKMO3htoerFtaA3qMzBhdK3+0cmRy5VSR43kAsKfINjwGrYv79DFjEYhMXmABi5UkI7AJiGWoJTAKRg6TAFLAlAuSHyGJdpMlPlEUsXT0Ce4FtkTNUHxCL2oG4+n7v3kUC/RRaTIs4+pG0iLUvLTADxIuR6XghTd5DWUPAPep8nsNkZjqOhxoBHoee/CiZ2Y7HhhoD4m5k5ziZQrCWu5oAVkNnnyRTDL4SrrJAzJHsnCJTCtZ9VzlgO3TMaTLlSJbywHCWZshUgm+Mq1lgPXTMAhkE38ttUEWHsrNEZiHSWXYoOytkFiOdcw5l5zyZpUjngkPZuUhmJdK55FB2LpNZjXSuOJSdq2TORTrXHMrOE+1T3NY+GOFweHVe67yg4UUNw+Hw6pLWeVnDKxpe1fCahtc1vKHhTQ1vaVjV8LaG6xre0fCuhhsabmq4peE9De9r+EDDY23iHmqdjzTc1vCxhk803NHwy/6H3+s/DbmN/gKuWsor7goAAA==",
        [ 4] = "7goAAB+LCAALmW9qAv/t1UtPE1EUB/BzhlJa3m8opVBooTyKiRgf+EAUEKMoprIyIUFMY1hYE6Im+DXcu2DvV+hncCfR4KJbE7+AC7z/mXvH05mzcOfGk0xO5zd/2jL3zC1TUGdny7xiX7/noO95Qf/SFvR920/t9brNn9jcsb1eLAW9Ya8/s56z5y32fMuef/7FxPY9/9e/q15zsFhTz/aEdRwfbYd5wtcruxXaff32qPb8VbX2Jr9Ze3lYq+afVo/eHb6o2vDXjYNwve3neG4mbE9EZippz+3IUMr2NDXPnJvFTtu7bO+2vSf8L9l+lPkOGarsPKlUCK/8J2At2Uvn533mCC5b7DcwAPQEDhoYArYIHDYwAkwIHDWQAbYKHDOQBSYFjhvIAdsEThiYBKZ8/OFj3sAUMC1w2kAB2C6waGAG2CFw1kAJ2Onj6nfgnIF5YJfABQOLwG4ft30sG1gC+reOGV+Uvpml4CytRKaKgdhS5BPuAfe8P+sfzIBBbDYtAhNA7DwJga1AbEMn4u+TwLqYmWBuDCLVJjAFxOSkBKaB2LrSAtuBDTFnqA4gNrW6SHYCc2ICgyk0iO2uS2A3cEvMZjCfBvFg9DQ9kJzzUVYfMEfN97OfeCLcV10NAHHrZHKQeLLptqGGgPXIew4T58O93NUIEAsnk6PEU+GvhKsM8DSy7mPE0+G+7yoLbEQ+fZy4EJulHDA6SxPExfA3xtUk8Djyv+eJMfj+3IY15VAmp4lLsWTBoUwWiediyRmHMjlLPB9LlhzK5BzxYiw571AmF4jLseSiQ5ksE1+IJZccymT0Vjb9ljdVdDj8uqgllzW8pOGWhpc1vKLhVQ2vabii4XUNb2h4U8NbGq5qeFvDNQ3vaHhXw3UNNzTc1PDeXy/HfQ33tbF5oCUfarit4SMNH2u4o+Gngw8/1xpM7qDf0brMce4KAAA=",
        [ 5] = "7goAAB+LCAALmW9qAv/t1UtPE1EUB/BzSikUCi3vNy3vN4mIr6oIAmoUxdRuWIppDAtKQtQNG7+DexM+gN/FtQYXbk1csHWB9z9z7uR05uzdeJLJaX/9lyl3ztxhCuviYo0/NoWvT1vCfiK9KJnzVNgPxAvSy/L5urw/47Bfiq+KH0tPi1fl/dc/TCzf+V//rgru8JcB1zQlPS2O47P0tHzufbtSrVD15P1p/fVxrf6utFt/e1SvlV7VTj8cvalJ+NvOYXS95TwyUiSjF82Gn6mMdBkVapWejc2cn8WceIf0Tun56L9kOZX7DUNU2X9ZqRBeBXfAZqZAV1dd7gg/Fux20ANMKex10AdsUtjvYACYVjjoYAjYrHDYwQgwo3DUwRiwReG4gyKwNcBfAZYcTACzCicdTAHbFE47mAG2K5x1MAfMBbjxAzjvYAHYoXDRwRKwM8C9AJcdrACDpWPGD6Xv7hLwCPldJFpvILYUfYengNhfUgqbgEU1DOFAOMTOk1bYDMQ2VFaYAWIOMgpbgGU1QOEQOcTktCrMArF1ZRW2AS/VnKHagdjUCgpzQOxwOfX1DmBajWM4kg6x93UqzANxY+QbbkgeC1BXF/A4tp7dxOMNK4TqAWLpdLKXuNiwbKg+YCH2N/uJS9Fe7msAeBJLDhJPRE8JX0PAg1hymHgy2vd9jQAvqTE5SjyVmKUxYHyWxomno2eMryJwPZYsEWPwg7mNasKjTk4SzyWSUx51cpp4PpGc8aiTs8QLieScR52cJ15KJBc86uQi8XIiueRRJ5eJVxPJFY86GV/Khmd5Q51ZD+1rVnLNwusWVq2z37CSNy28ZeFtC+9YWLbwroX3LLxv4YaFDyzctHDLwocWblu4Y+GuhY8sfGzhEwvPUwY+tZLPLNyz8LmFLyzct/DL4affmz+Z/EF/AXLytvXuCgAA",
        [ 6] = "7goAAB+LCAALmW9qAv/t1ctu01AQBuCZJI2TXtL7Nb2k1/QWJIq4Qym0AQQlRaFiT1GEuiBIVcuLsOcFIpY8QlfdsAaVBatKSLwAi3J++xx3bM+CHRtGsib+8rd27PExU1Cnp2t8Yj9XvaB7trPtNft9y/ZjDnrDfl+x/cjtl4P+0u6f2b87tPufbwX9y28mtv/rf/276sG9pot7mrI9Yx3bR9thKeGb9V3P2313dNB89bbRPCxVm2/2m43Si8bB+/3XDRv+urUX3m97nJTtadszFJ2prN23u5SzPU/RmXOz2Gm9y/aC7d3hr2R7KHMOo1TfeV43Y24++U/ARraHzs97zRZ8bbHPQD8wJXDAwCAwLXDIwDAwI3DEwCiwTeCYgSIwK3DcwARQntKkgSlgzsefPpYMTAPzAmcMzALbBc4ZmAd2CFwwUAZ2+rj+HbhoYAnYJXDZwAqw4OO2j6sGKkACMuNE6Zu5FVykk9hUMRBLinzCU0DPu7j/wQwYxGKTFpgB1sRkoNqALTEmwagYxBxkRdIDIiWCuJBFf3JyAvNALF15ge1+snwxZ6gOIBa1Y/GTOoFnYgKDKTSI5a5LYAGIta8gsBuIB6M78kDyhI+yeoFnFL2efcST4brqqh9YiyUHiKcilw01CMSvkckh4lK4lrsaBnqxuzlCPB2+JVyNAluxo48Rz4TrvqsiEBdZJseJZxOzNAGMz9Ik8Vz4jnE1BazEkiViDL4/t2FNO5TJGeJyIjnrUCbniBcTyXmHMrlAvJRIlh3K5CLxSiK55FAml4lXE8kVhzK5Snwpkaw4lMn4pYy8yyN1pCUva6+WNQ2vaBgfDr+uaslrGl7X8IaGNzXUDk63Nbyj4V0N1zW8p+GGhvc1fKDhpoZbGlY1fKjhIw0fa1jT8ImGTzXc1vDZXx9oR8NPex9+bfxgchv9AbAYmZPuCgAA",
        [ 7] = "7goAAB+LCAALmW9qAv/t1ctPE3EQB/CZshTK+/1+tDxbHiZifCuigBpFMcg/INIYDtaEqP+Id8OJxIN/Agl/ggcu1eDBhNTExFPDxQPO7G9+m2F37l6cZDPtZ6fddve7v0VwdXy8gCfy+lOd62V5/zPl+mLa9T3ZX5H9p9K3xY/Q9ar4gfT6wPVDeb8v/csfBJTP/K9/V220obqmKemBOG8fpQey3/vyxmYZNt+82y29eF0svc2ull7tlIrZ58Xd9zsvizL8dWUrut5yHIkW1EgPYpmSyEGdz5D0TCxzPotN0pult0hvjf4lyqHoN/TDxvqzDYo5vQrvgKV0G5ydtdPmdgt2EHQyphR2EXQz1ijsIehlDBT2EfQz1iocIBhkTCscIhhmrFM4QjDKWB/irxCzBDnGjMIxgnHGBoUTBJOMjQqnCKYZm0Jc/M6YJygwNiucIZhlbAlxLcQ5gnnG8NQh8g+Fb3QpcBBOYqlCRl5S9B2eYiyr6+8yQMiLTY3CgJFXnkBhLSMvQ6cK04wVlRmXG8JTFSAXIkJOTr3CDCMvXRmFDYxVlTOuRsYDFToXPEJe4ZoUNjMeqji6SBLuq2y6fBLyjdF67obE4RB1tTPygTR3AI5E66qvTkY+dXqyC3D03Gnj6maswPnJHsBstJb76mUsxyb7AHPRU8JXP+Ne7LoPAI5F676vQcZq7DuHAMcTWRpmjGdpBHAiesb4GmXcjk1mATn4YW6jynnUk2OA04nJcY96cgIwn5ic9KgnpwALiclpj3oyDzibmCx41JMzgHOJyVmPenIO8EJict6jnjywng1VC4+sh/ZFa3LBwksW7lt42cIrFl618JqF1y28YeFNC29ZeNvCRQvvWLhk4V0L71m4bOGKhasW3rfwgYUPzb+ZNvCRNfnYwjULn1j41MJ1Cz9vffi99APBb/AXIlopE+4KAAA=",
        [ 8] = "7goAAB+LCAALmW9qAv/t1UlPFFEQB/CqYZgFGPZtWId9N5GJuIIooERRDJJwFjIxHBwTot74FN69c/DCByGeNWiiRxO/gAd8/+56bU13HbxxsZJOTf/mPz0Nr/oNU1jn54u8Ka+/cdiPs2E/Ez+V8z3pJ+It6bCX68K+LO+XpRcldyDnP+T6S+KffjOx2P+6vGp1B6s1TUlPi+P4IB2WUr62s3uc3X399qj64lWl+qa0UX15WK2UnleO3h0eVCT8eX0/Wm/5npR0GR1Kx2YqI+cyOpSTnqfamfOz2CS9IL3ZXy/6K1k+4e6hSDvbz3bcmLtXwROwmmmli4s2d4RvC7Y76ACmFHY66ALWKex20ANMK+x1UATWK+xz0A/MKBxwMAjUtzTkYBiYC/BngCUHI8C8wlEHY8AGheMOJoCNCicdTAGbAlz5Cpx2MAMsKJx1MAdsDnArwHkHC0ACMuNG6YtbI+6nzdhUMRBbin7CU0DsLymFdcAzNQzhQDjEzpNWWA/ENtSiNAM8UTMTzo1DpLIKc0BMTk5hHoitK6+wIUhm/84ZqhFYVEMXDp5D7HBNCgtAbHcFhc3AJTWb4Xw6xIPRUvNA8mCAutqA+CLN7cRD0b7qqwN4Gkt2Eg/X/NtQXcATqk12E5eivdxXD/A4ds1e4pHoV8JXEbgXS/YRj0b7vq9+YDmWHCAeS8zSIDA+S0PE49FvjK9hIBZNJ0vEGPxgbqMa8aiTo8RTieSYR50cJ55OJCc86uQk8UwiOeVRJ6eJ5xLJGY86OUs8n0jOedTJeeIrieSCR65ZOKPKWQOXLbxqfXzRvKaFSxZe++fkdQtvWHjTwlsW3rbwjoXLFq5YeNfCVQvvWXjfwjUL1y3csPCBhQ8t3LTw1BqGR1bysYVbFj6x8KmF2xZ+3H//a/U7kz/oD67tb+DuCgAA",
        [ 9] = "7goAAB+LCAALmW9qAv/t1UtPE1EUB/BzWmgLlPebFihQyttEjM8qokA1imIQPoCYxkDSmhD1i7j3G7h2zydwrcGFCSsTd6xc4PnP3Dueds7CnRtPMjkzv57O494zd5jCOD1d5YTbL7l8ngzzp1SYa85POMxnLlM6TBV3eORy2eWC+73X5bzzfXf8+RcT+3P9j38WPbJF0yBzk3C5xTm2Dy7DEso3dvfOk3uv3x7XX9Sq9TeFrfqrw3q18Lx6/O7wZdUVf9k8iObbXcb3nGu14Ly6p1KNh5RxuY0ae873YtYdd7rc5XJ39JTsLiX3MEq7O892pc1lL3gD1lM9dHHRK1v4s8M+gX5gQuGAwCBQ/31IYBjYonBEYBTYqnBMIAdMKcwLjAPTCicEJoGZAH8EWBCYArYpnBaYAbYrLArMAjsUlgTmgNkA174B5wUWgJ0KFwWWgF0Bbge4LLACJCAzbpS+yhxxLprRaLyBJd1awbQLYn3R1UkgFpukwhZgTXeGRCswWIbSfzAFRB+kVGUaiCpViIHMBZ2TUdgGPFKthWgHllWfITqAWNTO1CNlgVjhsqqyE5hX7Ri2pCDWvi6F3UC8GN0NLySPB6ijF4gLae4jnojWVR/9wFrTyA8QTzaOhsQgEE+jK4eIC9Fa7mMYiInTlSPEU9FXwsco8KTpnGPE09G67yMHLDfdZ554JtZL48DmXpogLkbfGB+TwEpTZYEYjR/0bRRTHnXlNPFcrHLGo64sEs/HKmc96soS8UKscs6jrpwnXopVLnjUlYvEy7HKJY+6cpn4UqxyxWPDKKWNb0PZ+mAcWXjZwlULr1i4b139qlV5zcLrFt6w8KaFt+hvn/22hXcsXLPwroXrFt6z8L6FGxZuWrhlYcXCBxY+tLBm4SMLH1u4beETC59auGPhx4P3P9e/M/mNfgMiHOgv7goAAA==",
        [10] = "7goAAB+LCAALmW9qAv/t1c1OE1EUB/BzyqSFhJaPEkopH9MPoKUra1TqB2IBNYpiKi8gpjEsrAlRtzyEe9/At+ABXGtw4dbEXcPCRT1n7rnlTHv2brzJ5HZ+/aedzv3PLYIbFxcNzMrrS5mDwM1Tcp5Jubkl582Em8/RzTnxLTkP5bwi85l4Qz6nJ/71DwLKe//HvxvTdPhl4DVNyByI8/FZZraE8t32URuO3n047b562+m+D/e7b066nfBl5/TjyeuOhL/tHQ/WW75HKgRjvnMQ71RSzqUyMC7zBMQ757s4Kedp31mId5iuVr6KriEP7cMX7Tbwq+gJ2ElOQ78/Q4d7W3CWIMuYUDhH0GIcUzhPkGMMFC4Q5BnP8QoXCQqMSZVcIlhmTClcIVhlHI/wV4QhQZFxQmGJoMyYU1ghWGNsJq5wnWCDcTJKbv9grBLUGNMKNwnqjJkIDyI8w34/CAijW4fIFwrfaY2wANmhViHjpaqWW3ZC3l8SCscYp1QZXCEIeecJFNKNLETb0Ln60CQnuQdJlUwxciqlcJwxp9rkGkXIW9eEwhxjqHrmukZYUaVzxSPkHW5SJdOMvN2lFWYYe6qbrp+E/GBMxR5IXI5QjxlG/iLNs4Arg33Vjywj3zqdnANcjd02t6MT8q/RPg8YDvZydUPCaOG0LwAWYxfOI8/YGlr3RcDSYN/3o8AYDiWXAMsjXVpmHO7SCmBl8B/jxypjbigZAnLxo94ORtGjTpYAN0aSZY86WQGsjiTXPOrkOmBtJLnhUSergPWRZM2jTm4C8rMYT9Y96uQZYmXkM4NAMP6LjBFauGX9aV+zkg0Lr1vYs/CGhTctvGVep4VNC29beMfCuxbes3DbwvsW7lj4wMKWhbsW7lm4b+FDCx9Z+NjCTMrAJ1byqYUHFj6z8LmFhxZ+Of70e+cngj/gL1V2dcnuCgAA",
        [11] = "7goAAB+LCAALmW9qAv/t1cluE0EQBuAq2/EiOYuTKJvjxHEWZzlhBAEMISYORBAIMnkBgiyUA0aKgHfgzJ034FUizlhBFlckXoBDqH+me1Ie14EbF1oa1czn6ulxd00PU9guLmpccOdVF4uZMH7kMDacn7vroUQY6+665+K281IqjF3Xr+nuV3PXORe//mZi1/d/+3dtTA6/DFjThIsp5zg+uwhLKN9rHbfo+O37s87LN+3Ou/J+5/Vpp11+0T77cPqq7ZK/NU+i9XbjuFKhpIsp6q+ptLt2pUPZWO30YrWYdz7s4oiLo9G/ZDeUPMMstY6et1qEs+AN2E2P0eVlQY7wZ4fjAhPAhMJJgXMWTCqcEpxGZkrhjMAssM5XOCdQBKZV5rxACZhRuCCwCMwG+DPAssASMKewIrAM7PEVrgisAocSV7gmUAXmg+4734HrAhvAYYWbAlvAkQAPA2xm5OHloGDqmPGg1JU14iIVYlXFwKoqrXDZBbG/JBQmgdhskgpTwIaqjLA6BLEN1dVN08hEHaRVZgaIrIzCLBCVk1WYA2LryinsYSDsYz01kExkMdjUhtTT59EdO1xedR8G1lQ5hiUpmFO1GdanIF6M0b4XkksB6lYAYiDN48QL0b7q2wSwEZv5SeLFvmkLd3RB/BvtU8TlaC/3bRpYjI0+Q7wUfSV8mwVijbTPEVf6Jjj4zgAxyTpznnh5oJZKwHgtLRCvRN8Y3xaBvdjoZWIUflC3UVvyqDMrxNWBzGWPOnOFeH0gc9Wjzlwj3hjIrHrUmevEWwOZGx515iYx3sX+zC2POrOZ4e7APYsedWbX+jaUUgbGiyNo16zuNQuvW5iz8IaFNy3ctvCWhbctvGNh3cK7Ft6zcMfC+xbuWtiw8IGFexY2Ldy38KGFjyw8+OvnfGzhEwsPLXxq4TMLjyz8cvLp1+4PJn/QHzUgFLTuCgAA",
        [12] = "7goAAB+LCAALmW9qAv/t1U1PE2EQB/CZbVNK0vJWAoXyUqBACycxKr4UUVg1iGIq6Y2DmMZwsCZE/SLePXsxfAX4Bp41mODV1C/gAWf2mWedbufuxSdppv3tv+2+zM4iuHV+voaf+tz79cDVIrq6I5mDjKtN2d4RL6RdDeXzidQz+f6p1Lp8Tz7Chbz58hsBPf5f/2wNqWvD1zSQmhbn10epbIHyrcZ+A/bfvDtuv3jdar8th+1XR+1W+Xnr+P3Ry5aEv24fxtdb/kdaAlJS04mekpYDaU3ISu2H7p7zvZiTmpc6IHUwPkpM+S7ECWjsPWs0gN9Fd8BmZgguL4fp5TYLjhAUGAOFowTNgDClcIxwnJNphUWCCcZC+i9OEpQYMyo5RTDN2KdwhmCWMRvhzwjLBHOM/QrnCRYYQ4UVgkXGjsIlgmXGXIQb3xmrBDXGvMIVglXGgQh3I6zTcReRMDp1iLyj8I2uEZbAT5H4fDPySNF3eMDI8yVQmGLcUc3gGoKQJ09aIZ3IUjSGCkoznOyonnF9Q8gpvVdZxlB1k+sowhPVWq69CHmOhQo7jDzUOgpzjDzhcgrzjKja0bUkIc++AYWDjHxjDHbdkDgdoV7DjPXE+RwBnIHkCC0w8qnTG0YBZ7tOm5vohB3oTo4BluNZ7tc4I184nSwCzsVPCb8mGJuJ/ZwEnI/nvl8lxrPEb04BLvT00jRjspfowCvxM8avWcYwcURlQG78qG/jNedRJ+cBl3uSCx51sgJY7UkuetTJJcBaT3LZo05WAVd7kjWPOrkCyPdid3LVo07WAzzFZLKIgjp5aj2Kzyw8sZ4iVyxcs/CqhRfWH12zktctvGHhuoU3Lbxl4W0L71hYt3DDwrsWblp4z8L7Fm5ZuG1haOEDCx9a+MjCg4yBO1bysYW7Fj6x8KmFexZ+Pvzwa/MHgn/BHy3oFu3uCgAA",
        [13] = "7goAAB+LCAALmW9qAv/t1c1OE1EUB/B72kk/AgWmkFIoLWVaPgosKkbrJ6J8aBTFVF5ATKMsrAlRn6OJe9/AB+mCxLUGF25NfAEX9Zy55w6n07N3400mp/Obk9527n/ugLHj4mILcvw5zbXH9Q1DwOdFz9YWn4/xuc/1HGwt8PU81z77Jp83uX79Awb42v/x78YUHiDWNMHVY6fjM1eyhPDd9nHbHL/7cNZ9+bbTfV/d774+7XaqLzpnH09fdbj5295JtN48T4JrkqsXy1QqlskM16wZzpzL4jhXl+UJrpPRvwSeCn/DnGkfPW+3DX0Kn4Cd1JQZDHw87GXGPMI0YULgDELRQ0wKLCDOUqcnsIgwRzjmXeI8QokwJToXEMqEaYEVhEXCTIi/QqwiLBFmBQYINULfu8Q6wjJhS3SuIKwSjoe4/YNwDaFBmBO4jrBBOBHiYYh9GAx6hOGtA6Afar7jGkEpuvPR/SZMi2jZZUfsifW3GUCkzSYp0CMMRDJsOhBpGxoTmqLOlsiMzQ0idaUFZggpORmBWULaurICfZqoIHJms4aYF6GzwUOkHW5cYI5wU8TRRhKxKbJp84lID8bk0AMJ5RDl8AlpIsl5A5VoX3VjmjCI3fkZA4tDt83u6IitWGfBQDXay92YJezFOosGlqK3hBtzhLRGsnPeQBDt+26UCAux71wwUBvJUpkwnqWKgXr0jnFjkdCPzV41QMEPcxuNJYeyMzCwOtJZcyg76wbWRjqXHcrOFQONkc5Vh7JzzcDGSGfDoexcN0DP4nDnhkPZ2QfIj3xnzzAOZ0kZBQ3PtZf2Fa1zS8OrGjY1vKbhdQ1bGt7Q8KaGtzS8reEdDe9quK3hPQ13NLyv4QMNdzXc03BfwwMNH2r4SMNAw8caPtHwUMOnGj7T8EjDLyeffu/8BOMO8xdCX+gF7goAAA==",
        [14] = "7goAAB+LCAALmW9qAv/t1UtPE1EUB/B7+uDR8obyKn0ABQrNtBHxrViFqlUUU/kCYhrDwpoQ9Yu49xv4XVxrcOESiSlh6QLPmXvu5DBz9m64yeTf/nrSaWf+MwPGrqOjddjg18ecDbB5wu9rnFXOFGeSM82Z4Wxydjk9zjPOU85vf8EA7+ty/b81ghuIcxrjTLDT9oUzwZ8732rttcze+4+Hndfv2p0PxUbn7UGnXXzVPvx08KbNw9+394PzzfuJccY5E6FO9XD2cvZx9oc657o4wDnIOcQ5HPxL4F3hb5gxrd2XrZahV/4VUO8ZMefno7jZjxnHEMYJYwInEKqEcYGTCFOECYHTCDOESYGzCFnCHoFzCDnCXoF5hAJhn4+/fSwizBP2C1xAWCRMCywhLBGmBC4jrBAO+Lj5k7CMsEo4KHANoUI45OOOjx5CAxD9QwdAP9T8wHMEWbMRahUQHotq2dOOSPeXmMA44Ykogy0EYk00w7YDsSpqYquCmBKdsb1BTIoC2RIhpkWbbKMQM6Jatl6ITdEz2zXEriidLR6iJxpoW4h4JupoK4l4Krpp+4lIF8bwhQsScj7KNUrohY7nmIF8cF91a5ywFpqcMFC4cNjsHR0xFZqcNFAM7uVuTRHSiZOT0wbmg6eEWzOE1dB3zhpYCO77bmUJm6HJOQOLkS7lCMNdyhsoBc8YtwqE6dBk0QAV3+9tsOYdyskFAyuRyUWHcrJkoByZXHIoJ5cNrEYmVxzKybKBSmRy1aGcXDPgRSYrDuWkZ6AbmWwAo5zsas+GpoYZDa9ouK7hVQ1PNbym4XUNb2h4U8NbGt7W8I6GdzW8p+Gmhvc1rGv4QMOHGm5puK1hQ8NHGj7W8ImGNQ2favhMwx0Nn2v4QsNdDb/uf/5T/wXGbeYf9XLcPu4KAAA=",
    };

    private static string GetBracketData(int tournamentId) =>
        BracketBlobs.TryGetValue(tournamentId, out var blob) ? blob : BracketBlobs[14];
    internal static string CatalogJson()
    {
        var sb = new System.Text.StringBuilder("{\"tournament\":[");
        for (int i = 0; i < Defs.Length; i++)
        {
            var (id, _, _, diff, coins, unlock) = Defs[i];
            int trophy = 8200000 + id;   // trophy doc lives at fut/items/pc/<trophy>.json
            if (i > 0) sb.Append(',');
            string lockState = unlock > TrophiesWon ? "LOCKED_TROPHIES" : "UNLOCKED";
            sb.Append("{\"id\":").Append(id).Append(",\"tournamentId\":").Append(id)
              .Append(",\"tournamentType\":0,\"type\":\"offline\",\"numTeams\":16,\"numRounds\":4,")
              .Append("\"numMatches\":4,\"matchlength\":6,\"starttime\":0,\"timeUntilStart\":0,")
              .Append("\"timeUntilEnd\":31536000,\"trophyResourceId\":").Append(trophy)
              .Append(",\"trophyUserCount\":").Append(TrophiesWon).Append(",\"triesMax\":0,\"treeType\":0,\"lock\":\"").Append(lockState).Append("\",")
              .Append("\"unlockreq\":").Append(unlock).Append(",\"nextReset\":0,\"visStart\":0,\"visEnd\":0,\"rounds\":[");
            for (int r = 1; r <= 4; r++)
            {
                if (r > 1) sb.Append(',');
                sb.Append("{\"id\":").Append(r).Append(",\"difficulty\":").Append(diff)
                  .Append(",\"rewardMultiplier\":1,\"coins\":").Append(r == 4 ? coins : 0).Append('}');
            }
            sb.Append("],\"awardSet\":{\"awards\":[{\"awardType\":1,\"value\":").Append(coins).Append(",\"halid\":0}]}}");
        }
        return sb.Append("]}").ToString();
    }

    internal static string TrophyJson(int tourneyId)
    {
        string name = "Cup"; int design = 1100;
        foreach (var d in Defs)
            if (d.Id == tourneyId) { name = d.Name; design = d.Design; break; }
        return "{\"tournamentId\":" + tourneyId + ",\"tournamentType\":0,\"assetName\":\"trophy_" + design +
               "_gold\",\"silName\":\"trophy_" + design + "_dark\",\"locString\":[{\"lang\":\"ENG_US\",\"label\":\"" +
               name + "\"}]}";
    }

    internal static int ActiveTournamentId = 1;

    internal static int TrophiesWon => FutProfileStore.Get().TrophiesWon;

    internal const int NumRounds = 4;                       // every fixed cup is a 16-team, 4-round bracket
    internal static int? CurrentMatchTournamentId = null;   // set on POST /match for a tournament match
    internal static int CurrentRound = 1;                   // round the client last saved = round being played

    internal static int AwardCoins(int tournamentId)
    {
        foreach (var d in Defs) if (d.Id == tournamentId) return d.Coins;
        return 0;
    }

    internal static string TeamsJson(int tournamentId = 0)
    {
        int tid = tournamentId > 0 ? tournamentId : ActiveTournamentId;
        int[] ids = GetTeamIds(tid);
        string arr = "[" + string.Join(",", ids) + "]";
        return "{\"team\":" + arr + ",\"teams\":" + arr + ",\"teamIds\":" + arr + ",\"teamId\":" + arr +
               ",\"entries\":" + arr + ",\"list\":" + arr + ",\"data\":" + arr + ",\"results\":" + arr +
               ",\"totalResults\":" + ids.Length + "}";
    }

    internal static string UserTournamentJson(int id)
    {
        var saved = FutProfileStore.Get().SavedTournaments.GetValueOrDefault(id);
        string blob = saved is { TournamentData.Length: > 0 } ? saved.TournamentData : GetBracketData(id);
        int round = saved?.Round ?? 1;
        return "{\"round\":" + round + ",\"dataVersion\":2,\"tournamentData\":\"" + blob +
               "\",\"tournamentProgress\":1,\"tournamentCoins\":0,\"tournamentRoundAwards\":[],\"awardSet\":{\"awards\":[]}}";
    }

    internal static string UserListJson()
    {
        var saved = FutProfileStore.Get().SavedTournaments;
        if (saved.Count == 0) return "{\"tournament\":[]}";
        var sb = new System.Text.StringBuilder("{\"tournament\":[");
        bool first = true;
        foreach (var kv in saved)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"id\":").Append(kv.Key).Append(",\"tournamentId\":").Append(kv.Key)
              .Append(",\"tournamentType\":0,\"round\":").Append(kv.Value.Round)
              .Append(",\"dataVersion\":2,\"tournamentData\":\"").Append(kv.Value.TournamentData)
              .Append("\",\"tournamentProgress\":1,\"tournamentCoins\":0,\"tournamentRoundAwards\":[],\"awardSet\":{\"awards\":[]}}");
        }
        return sb.Append("]}").ToString();
    }

    internal static void SaveProgress(int id, int round, string tournamentData, int progressDataVersion, string progressData)
    {
        FutProfileStore.Mutate(p =>
        {
            var s = p.SavedTournaments.TryGetValue(id, out var e) ? e : new SavedTournament();
            if (round > 0) s.Round = round;
            if (!string.IsNullOrEmpty(tournamentData)) s.TournamentData = tournamentData;
            if (progressDataVersion > 0) s.ProgressDataVersion = progressDataVersion;
            if (!string.IsNullOrEmpty(progressData)) s.ProgressData = progressData;
            p.SavedTournaments[id] = s;
        });
    }

    internal static void ClearProgress(int id) => FutProfileStore.Mutate(p => p.SavedTournaments.Remove(id));
}
