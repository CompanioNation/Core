// Static-shell localization for index.html and loading.html.
// These are plain static HTML files (not Blazor components), so they cannot use
// IStringLocalizer. This script localizes the pre-Bootstrap shell at runtime.
// NOTE: production SEO is handled separately by the SSR-rendered App.razor and
// /s/* server-rendered pages, which emit localized title/meta/canonical/hreflang.
(function () {
    'use strict';

    var supported = ['en', 'es', 'pt', 'fr', 'zh', 'ja'];

    var translations = {
        en: {
            loadingTitle: "CompanioNation\u2122 \u2014 Loading",
            tagline: "Online Dating for People, Not Profits",
            startingUp: "Starting up \u2014 just a moment\u2026",
            refreshHint: "This page will refresh automatically.",
            appTitle: "CompanioNation\u2122 \u2014 The First Open-Source Dating App | No Paywalls, No Swipe Games",
            appMetaDescription: "CompanioNation\u2122 is the first ever open-source dating app \u2014 fully transparent so you know what really happens behind the scenes. No paywalls, no swipe games. Just message people you're interested in. The LINK algorithm keeps bots and scammers out. CompanioNita, your optional AI assistant, helps you communicate better. CompanioNation profits from your success, not your clicks."
        },
        es: {
            loadingTitle: "CompanioNation\u2122 \u2014 Cargando",
            tagline: "Citas en l\u00ednea para personas, no ganancias",
            startingUp: "Iniciando \u2014 solo un momento\u2026",
            refreshHint: "Esta p\u00e1gina se actualizar\u00e1 autom\u00e1ticamente.",
            appTitle: "CompanioNation\u2122 \u2014 La primera app de citas de c\u00f3digo abierto | Sin muros de pago, sin juegos de deslizar",
            appMetaDescription: "CompanioNation\u2122 es la primera app de citas de c\u00f3digo abierto \u2014 totalmente transparente para que sepas lo que realmente ocurre entre bastidores. Sin muros de pago, sin juegos de deslizar. Simplemente env\u00eda mensajes a las personas que te interesan. El algoritmo LINK mantiene alejados a los bots y estafadores. CompanioNita, tu asistente de IA opcional, te ayuda a comunicarte mejor. CompanioNation se beneficia de tu \u00e9xito, no de tus clics."
        },
        pt: {
            loadingTitle: "CompanioNation\u2122 \u2014 Carregando",
            tagline: "Namoro online para pessoas, n\u00e3o lucros",
            startingUp: "Iniciando \u2014 s\u00f3 um momento\u2026",
            refreshHint: "Esta p\u00e1gina ser\u00e1 atualizada automaticamente.",
            appTitle: "CompanioNation\u2122 \u2014 O primeiro app de namoro de c\u00f3digo aberto | Sem muros de pagamento, sem jogos de deslizar",
            appMetaDescription: "CompanioNation\u2122 \u00e9 o primeiro app de namoro de c\u00f3digo aberto \u2014 totalmente transparente para voc\u00ea saber o que realmente acontece nos bastidores. Sem muros de pagamento, sem jogos de deslizar. Basta enviar mensagens para quem lhe interessa. O algoritmo LINK mant\u00e9m bots e golpistas longe. A CompanioNita, sua assistente de IA opcional, ajuda voc\u00ea a se comunicar melhor. A CompanioNation lucra com o seu sucesso, n\u00e3o com os seus cliques."
        },
        fr: {
            loadingTitle: "CompanioNation\u2122 \u2014 Chargement",
            tagline: "Rencontres en ligne pour les gens, pas les profits",
            startingUp: "D\u00e9marrage \u2014 un instant\u2026",
            refreshHint: "Cette page se rafra\u00eechira automatiquement.",
            appTitle: "CompanioNation\u2122 \u2014 La premi\u00e8re appli de rencontre open source | Sans abonnement payant, sans jeux de balayage",
            appMetaDescription: "CompanioNation\u2122 est la toute premi\u00e8re appli de rencontre open source \u2014 enti\u00e8rement transparente pour que vous sachiez ce qui se passe vraiment en coulisses. Sans abonnement payant, sans jeux de balayage. Envoyez simplement des messages aux personnes qui vous int\u00e9ressent. L'algorithme LINK tient les robots et les arnaqueurs \u00e0 l'\u00e9cart. CompanioNita, votre assistante IA optionnelle, vous aide \u00e0 mieux communiquer. CompanioNation profite de votre r\u00e9ussite, pas de vos clics."
        },
        zh: {
            loadingTitle: "CompanioNation\u2122 \u2014 \u52a0\u8f7d\u4e2d",
            tagline: "\u4e3a\u5927\u4f17\u800c\u975e\u5229\u6da6\u6253\u9020\u7684\u5728\u7ebf\u7ea6\u4f1a",
            startingUp: "\u6b63\u5728\u542f\u52a8 \u2014 \u8bf7\u7a0d\u5019\u2026",
            refreshHint: "\u6b64\u9875\u9762\u5c06\u81ea\u52a8\u5237\u65b0\u3002",
            appTitle: "CompanioNation\u2122 \u2014 \u9996\u4e2a\u5f00\u6e90\u4ea4\u53cb\u5e94\u7528 | \u65e0\u4ed8\u8d39\u5899\uff0c\u65e0\u6ed1\u52a8\u6e38\u620f",
            appMetaDescription: "CompanioNation\u2122 \u662f\u9996\u4e2a\u5f00\u6e90\u4ea4\u53cb\u5e94\u7528 \u2014 \u5b8c\u5168\u900f\u660e\uff0c\u8ba9\u4f60\u4e86\u89e3\u5e55\u540e\u771f\u5b9e\u53d1\u751f\u7684\u4e00\u5207\u3002\u65e0\u4ed8\u8d39\u5899\uff0c\u65e0\u6ed1\u52a8\u6e38\u620f\u3002\u76f4\u63a5\u7ed9\u4f60\u611f\u5174\u8da3\u7684\u4eba\u53d1\u6d88\u606f\u3002LINK \u7b97\u6cd5\u8ba9\u673a\u5668\u4eba\u548c\u8bc8\u9a97\u8005\u8fdc\u79bb\u3002CompanioNita \u662f\u4f60\u53ef\u9009\u7684 AI \u52a9\u624b\uff0c\u5e2e\u52a9\u4f60\u66f4\u597d\u5730\u6c9f\u901a\u3002CompanioNation \u9760\u4f60\u7684\u6210\u529f\u83b7\u5229\uff0c\u800c\u4e0d\u662f\u4f60\u7684\u70b9\u51fb\u3002"
        },
        ja: {
            loadingTitle: "CompanioNation\u2122 \u2014 \u8aad\u307f\u8fbc\u307f\u4e2d",
            tagline: "\u5229\u76ca\u3067\u306f\u306a\u304f\u3001\u4eba\u3005\u306e\u305f\u3081\u306e\u30aa\u30f3\u30e9\u30a4\u30f3\u30c7\u30fc\u30c8",
            startingUp: "\u8d77\u52d5\u4e2d \u2014 \u5c11\u3005\u304a\u5f85\u3061\u304f\u3060\u3055\u3044\u2026",
            refreshHint: "\u3053\u306e\u30da\u30fc\u30b8\u306f\u81ea\u52d5\u7684\u306b\u66f4\u65b0\u3055\u308c\u307e\u3059\u3002",
            appTitle: "CompanioNation\u2122 \u2014 \u521d\u306e\u30aa\u30fc\u30d7\u30f3\u30bd\u30fc\u30b9\u51fa\u4f1a\u3044\u7cfb\u30a2\u30d7\u30ea | \u30da\u30a4\u30a6\u30a9\u30fc\u30eb\u306a\u3057\u3001\u30b9\u30ef\u30a4\u30d7\u30b2\u30fc\u30e0\u306a\u3057",
            appMetaDescription: "CompanioNation\u2122 \u306f\u521d\u306e\u30aa\u30fc\u30d7\u30f3\u30bd\u30fc\u30b9\u51fa\u4f1a\u3044\u7cfb\u30a2\u30d7\u30ea\u3067\u3059\u3002\u5b8c\u5168\u306b\u900f\u660e\u3067\u3001\u821e\u53f0\u88cf\u3067\u5b9f\u969b\u306b\u4f55\u304c\u8d77\u304d\u3066\u3044\u308b\u304b\u3092\u77e5\u308b\u3053\u3068\u304c\u3067\u304d\u307e\u3059\u3002\u30da\u30a4\u30a6\u30a9\u30fc\u30eb\u306a\u3057\u3001\u30b9\u30ef\u30a4\u30d7\u30b2\u30fc\u30e0\u306a\u3057\u3002\u6c17\u306b\u306a\u308b\u76f8\u624b\u306b\u30e1\u30c3\u30bb\u30fc\u30b8\u3092\u9001\u308b\u3060\u3051\u3002LINK \u30a2\u30eb\u30b4\u30ea\u30ba\u30e0\u304c\u30dc\u30c3\u30c8\u3084\u8a50\u6b3a\u5e2b\u3092\u5bc4\u305b\u4ed8\u3051\u307e\u305b\u3093\u3002\u30aa\u30d7\u30b7\u30e7\u30f3\u306e AI \u30a2\u30b7\u30b9\u30bf\u30f3\u30c8 CompanioNita \u304c\u3001\u3088\u308a\u826f\u3044\u30b3\u30df\u30e5\u30cb\u30b1\u30fc\u30b7\u30e7\u30f3\u3092\u30b5\u30dd\u30fc\u30c8\u3057\u307e\u3059\u3002CompanioNation \u306f\u30af\u30ea\u30c3\u30af\u3067\u306f\u306a\u304f\u3001\u3042\u306a\u305f\u306e\u6210\u529f\u304b\u3089\u5229\u76ca\u3092\u5f97\u307e\u3059\u3002"
        }
    };

    function detectLanguage() {
        try {
            var q = new URLSearchParams(window.location.search).get('lang');
            if (q && supported.indexOf(q) !== -1) return q;
        } catch (e) { /* ignore */ }

        try {
            var stored = localStorage.getItem('culture');
            if (stored && supported.indexOf(stored) !== -1) return stored;
        } catch (e) { /* ignore */ }

        var langs = (navigator.languages && navigator.languages.length)
            ? navigator.languages
            : [navigator.language || 'en'];

        for (var i = 0; i < langs.length; i++) {
            var base = (langs[i] || '').split('-')[0].toLowerCase();
            if (supported.indexOf(base) !== -1) return base;
        }

        return 'en';
    }

    function t(key) {
        var lang = detectLanguage();
        var map = translations[lang] || translations.en;
        return map[key] || translations.en[key] || '';
    }

    window.cnStaticShellI18n = {
        supported: supported,
        detectLanguage: detectLanguage,
        t: t,
        applyLoading: function () {
            var lang = detectLanguage();
            document.documentElement.lang = lang;
            document.title = t('loadingTitle');

            var tagline = document.getElementById('cn-loading-tagline');
            var starting = document.getElementById('cn-loading-starting');
            var hint = document.getElementById('cn-loading-refresh');
            if (tagline) tagline.textContent = t('tagline');
            if (starting) starting.textContent = t('startingUp');
            if (hint) hint.textContent = t('refreshHint');
        },
        applyIndex: function () {
            var lang = detectLanguage();
            document.documentElement.lang = lang;
            document.title = t('appTitle');

            var meta = document.querySelector('meta[name="description"]');
            if (meta) meta.setAttribute('content', t('appMetaDescription'));
        }
    };
})();
