#!/usr/bin/env python3
"""Export public NLTK inference data for the pure-.NET legacy-compatible normalizer.

Run with nltk==3.10.4 and downloaded stopwords, wordnet and
averaged_perceptron_tagger_eng. No application/Box documents enter this resource.
"""
import gzip
import hashlib
import json
from pathlib import Path
import nltk
from nltk.corpus import stopwords, wordnet
from nltk.tag.perceptron import PerceptronTagger

if nltk.__version__ != '3.10.4':
    raise SystemExit('Use nltk==3.10.4 to reproduce the validated inference model.')

root = Path(__file__).resolve().parents[1]
folder = root / 'dotnet/core/Assistant/Data'
folder.mkdir(parents=True, exist_ok=True)
wordnet.ensure_loaded()
tagger = PerceptronTagger()
model = {
    'stopwords': stopwords.words('english'),
    'classes': sorted(tagger.classes), 'weights': tagger.model.weights, 'tagDictionary': tagger.tagdict,
    'lemmas': {pos: sorted(wordnet.all_lemma_names(pos=pos)) for pos in ['a', 'r', 'n', 'v']},
    'exceptions': {pos: wordnet._exception_map[pos] for pos in ['a', 'r', 'n', 'v']},
    'substitutions': {pos: wordnet.MORPHOLOGICAL_SUBSTITUTIONS[pos] for pos in ['a', 'r', 'n', 'v']},
}
body = json.dumps(model, sort_keys=True, separators=(',', ':')).encode()
output = folder / 'english-nltk.json.gz'
output.write_bytes(gzip.compress(body, mtime=0))
(folder/'manifest.json').write_text(json.dumps({'nltk': nltk.__version__, 'sha256': hashlib.sha256(output.read_bytes()).hexdigest(), 'uncompressedBytes': len(body), 'sources': ['https://github.com/nltk/nltk_data/tree/gh-pages/packages/taggers', 'https://wordnet.princeton.edu/license-and-commercial-use']}, indent=2)+'\n')
print('Exported public NLP inference model:', output.stat().st_size, 'bytes')
