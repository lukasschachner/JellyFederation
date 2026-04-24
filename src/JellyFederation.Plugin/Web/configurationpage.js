(function () {
    'use strict';

    var pluginId = 'e5c0cda1-805e-41e2-9654-e17143dc31a1';
    var pageId = 'JellyFederationConfigPage';
    var defaultHolePunchPort = 0;
    var defaultLargeFileQuicThresholdBytes = 536870912;

    function getPage(element) {
        return element && element.closest ? element.closest('#' + pageId) : document.getElementById(pageId);
    }

    function setInputValue(page, selector, value) {
        var input = page.querySelector(selector);
        if (input) {
            input.value = value === undefined || value === null ? '' : value;
        }
    }

    function getTrimmedValue(form, selector) {
        var input = form.querySelector(selector);
        return input ? input.value.trim() : '';
    }

    function setChecked(page, selector, value) {
        var input = page.querySelector(selector);
        if (input) {
            input.checked = value;
        }
    }

    function parseNonNegativeInt(value, fallback) {
        var parsed = parseInt(value, 10);
        return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback;
    }

    function parsePositiveInt(value, fallback) {
        var parsed = parseInt(value, 10);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
    }

    function hideLoadingMessage() {
        if (window.Dashboard && Dashboard.hideLoadingMsg) {
            Dashboard.hideLoadingMsg();
        }
    }

    function showLoadingMessage() {
        if (window.Dashboard && Dashboard.showLoadingMsg) {
            Dashboard.showLoadingMsg();
        }
    }

    function showAlert(title, text) {
        if (window.Dashboard && Dashboard.alert) {
            Dashboard.alert({ title: title, text: text });
        } else {
            window.alert(text);
        }
    }

    function loadConfig(page) {
        showLoadingMessage();

        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config = config || {};

            setInputValue(page, '#federationServerUrl', config.FederationServerUrl);
            setInputValue(page, '#serverId', config.ServerId);
            setInputValue(page, '#apiKey', config.ApiKey);
            setInputValue(page, '#serverName', config.ServerName);
            setInputValue(page, '#publicJellyfinUrl', config.PublicJellyfinUrl);
            setInputValue(page, '#downloadDirectory', config.DownloadDirectory);
            setInputValue(page, '#stunServer', config.StunServer);
            setInputValue(page, '#overridePublicIp', config.OverridePublicIp);
            setInputValue(page, '#holePunchPort', config.HolePunchPort || defaultHolePunchPort);
            setChecked(page, '#preferQuicForLargeFiles', config.PreferQuicForLargeFiles !== false);
            setInputValue(page, '#largeFileQuicThresholdBytes', config.LargeFileQuicThresholdBytes || defaultLargeFileQuicThresholdBytes);
            hideLoadingMessage();
        }).catch(function (err) {
            console.error('[JellyFederation] Failed to load config:', err);
            hideLoadingMessage();
            showAlert('JellyFederation', 'Failed to load settings. Check the browser console for details.');
        });
    }

    function saveConfig(form) {
        showLoadingMessage();

        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config = config || {};

            config.FederationServerUrl = getTrimmedValue(form, '#federationServerUrl');
            config.ServerId = getTrimmedValue(form, '#serverId');
            config.ApiKey = getTrimmedValue(form, '#apiKey');
            config.ServerName = getTrimmedValue(form, '#serverName');
            config.PublicJellyfinUrl = getTrimmedValue(form, '#publicJellyfinUrl');
            config.DownloadDirectory = getTrimmedValue(form, '#downloadDirectory');
            config.StunServer = getTrimmedValue(form, '#stunServer');
            config.OverridePublicIp = getTrimmedValue(form, '#overridePublicIp');
            config.HolePunchPort = parseNonNegativeInt(getTrimmedValue(form, '#holePunchPort'), defaultHolePunchPort);

            var preferQuic = form.querySelector('#preferQuicForLargeFiles');
            config.PreferQuicForLargeFiles = !!(preferQuic && preferQuic.checked);
            config.LargeFileQuicThresholdBytes = parsePositiveInt(
                getTrimmedValue(form, '#largeFileQuicThresholdBytes'),
                defaultLargeFileQuicThresholdBytes);

            return ApiClient.updatePluginConfiguration(pluginId, config);
        }).then(function () {
            hideLoadingMessage();
            showAlert('JellyFederation', 'Settings saved.');
        }).catch(function (err) {
            var message = err && (err.message || JSON.stringify(err)) || 'Unknown error';
            console.error('[JellyFederation] Failed to save config:', err);
            hideLoadingMessage();
            showAlert('JellyFederation', 'Save failed — ' + message);
        });
    }

    function bindPage(page) {
        if (!page || page.dataset.jellyFederationBound === 'true') {
            return;
        }

        page.dataset.jellyFederationBound = 'true';
        page.addEventListener('pageshow', function () {
            loadConfig(page);
        });

        var form = page.querySelector('#JellyFederationConfigForm');
        if (form) {
            form.addEventListener('submit', function (event) {
                event.preventDefault();
                saveConfig(form);
                return false;
            });
        }
    }

    function initialize() {
        Array.prototype.forEach.call(document.querySelectorAll('#' + pageId), bindPage);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize, { once: true });
    } else {
        initialize();
    }
}());
